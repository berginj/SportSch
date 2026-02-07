#!/usr/bin/env python3
"""
Extract candidate action photography from a public AGSA site crawl.

This script only crawls public pages on the base domain, respects robots.txt,
and downloads images from an allowlisted domain set.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import sys
import time
from collections import deque
from dataclasses import dataclass
from io import BytesIO
from pathlib import Path
from typing import Dict, List, Optional, Set, Tuple
from urllib.parse import urljoin, urlparse
from urllib.robotparser import RobotFileParser

import requests
from bs4 import BeautifulSoup
from PIL import Image, UnidentifiedImageError

try:
    import imagehash  # type: ignore
except Exception:  # pragma: no cover
    imagehash = None


USER_AGENT = "AGSA-Image-Collector/1.0 (+public-crawl; contact: info@agsafastpitch.com)"
MIN_BYTES = 40 * 1024
MIN_LONGEST_SIDE = 600
PHASH_DISTANCE_THRESHOLD = 6
REQUEST_TIMEOUT = 20
MAX_RETRIES = 3

IMG_EXT_RE = re.compile(r"\.(jpg|jpeg|png|webp|gif|bmp|tiff|avif)(?:\?.*)?$", re.IGNORECASE)
CSS_BG_RE = re.compile(r"background-image\s*:\s*url\((['\"]?)(.*?)\1\)", re.IGNORECASE)
LOGO_HINT_RE = re.compile(r"(logo|icon|favicon|badge|wordmark|emblem|avatar)", re.IGNORECASE)


@dataclass
class Candidate:
    id: str
    filename: str
    sourcePage: str
    sourceImageUrl: str
    alt: str
    width: int
    height: int
    bytes: int
    phash: str
    phashClusterId: str = ""


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Extract candidate images from AGSA public pages.")
    parser.add_argument("--base-url", default="https://agsafastpitch.com")
    parser.add_argument("--max-pages", type=int, default=200)
    parser.add_argument("--max-depth", type=int, default=3)
    parser.add_argument("--rate-limit-ms", type=int, default=800)
    parser.add_argument("--output-dir", default="./public/images/candidates")
    parser.add_argument(
        "--allow-domains",
        default="agsafastpitch.com,cdn2.sportngin.com",
        help="Comma-separated hostnames for image download allowlist.",
    )
    return parser.parse_args()


def canonical_page(url: str) -> str:
    parsed = urlparse(url)
    path = parsed.path or "/"
    return f"{parsed.scheme}://{parsed.netloc}{path}"


def normalize_url(base_url: str, value: str) -> Optional[str]:
    if not value:
        return None
    raw = value.strip()
    if not raw or raw.startswith("data:") or raw.startswith("javascript:"):
        return None
    return urljoin(base_url, raw)


def same_domain(url: str, base_hostname: str) -> bool:
    return urlparse(url).hostname == base_hostname


def allowed_image_domain(url: str, allowed_domains: Set[str]) -> bool:
    host = urlparse(url).hostname or ""
    host = host.lower()
    for domain in allowed_domains:
        domain = domain.lower()
        if host == domain or host.endswith(f".{domain}"):
            return True
    return False


def parse_srcset(srcset: str, page_url: str) -> List[str]:
    urls: List[str] = []
    for part in srcset.split(","):
        token = part.strip().split(" ")[0]
        normalized = normalize_url(page_url, token)
        if normalized:
            urls.append(normalized)
    return urls


def extract_image_urls(html: str, page_url: str) -> List[Tuple[str, str]]:
    soup = BeautifulSoup(html, "html.parser")
    found: List[Tuple[str, str]] = []

    for img in soup.find_all("img"):
        alt = (img.get("alt") or "").strip()
        src = normalize_url(page_url, img.get("src", ""))
        if src:
            found.append((src, alt))
        srcset = img.get("srcset", "")
        for srcset_url in parse_srcset(srcset, page_url):
            found.append((srcset_url, alt))

    for meta in soup.find_all("meta"):
        prop = (meta.get("property") or meta.get("name") or "").strip().lower()
        if prop == "og:image":
            content = normalize_url(page_url, meta.get("content", ""))
            if content:
                found.append((content, ""))

    for node in soup.find_all(style=True):
        style = node.get("style", "")
        for match in CSS_BG_RE.finditer(style):
            url = normalize_url(page_url, match.group(2))
            if url:
                found.append((url, ""))

    # Preserve order while de-duping.
    seen: Set[str] = set()
    ordered: List[Tuple[str, str]] = []
    for src, alt in found:
        if src not in seen:
            seen.add(src)
            ordered.append((src, alt))
    return ordered


def extract_links(html: str, page_url: str, base_hostname: str) -> List[str]:
    soup = BeautifulSoup(html, "html.parser")
    links: List[str] = []
    for a in soup.find_all("a", href=True):
        url = normalize_url(page_url, a["href"])
        if not url:
            continue
        parsed = urlparse(url)
        if parsed.scheme not in ("http", "https"):
            continue
        if not same_domain(url, base_hostname):
            continue
        links.append(canonical_page(url))
    return list(dict.fromkeys(links))


def guess_extension(url: str, image: Image.Image) -> str:
    ext = Path(urlparse(url).path).suffix.lower()
    if ext in {".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".tif", ".tiff", ".avif"}:
        return ext if ext != ".jpeg" else ".jpg"
    fmt = (image.format or "").lower()
    if fmt == "jpeg":
        return ".jpg"
    if fmt:
        return f".{fmt}"
    return ".jpg"


def should_skip(url: str, width: int, height: int, num_bytes: int, ext: str) -> bool:
    filename = Path(urlparse(url).path).name.lower()
    longest = max(width, height)
    if ext == ".svg":
        return True
    if LOGO_HINT_RE.search(filename):
        return True
    if num_bytes < MIN_BYTES:
        return True
    if longest < MIN_LONGEST_SIDE:
        return True
    return False


def compute_phash(image: Image.Image) -> str:
    if imagehash:
        return str(imagehash.phash(image))
    # Fallback: average hash-like string.
    gray = image.convert("L").resize((8, 8))
    pixels = list(gray.getdata())
    avg = sum(pixels) / len(pixels)
    bits = "".join("1" if p > avg else "0" for p in pixels)
    return f"ahash-{int(bits, 2):016x}"


def hamming_distance_hex(a: str, b: str) -> int:
    if a.startswith("ahash-") or b.startswith("ahash-"):
        return 0 if a == b else 64
    try:
        x = int(a, 16) ^ int(b, 16)
    except ValueError:
        return 64
    return x.bit_count()


def choose_better(a: Candidate, b: Candidate) -> Candidate:
    a_score = (a.bytes, a.width * a.height)
    b_score = (b.bytes, b.width * b.height)
    return a if a_score >= b_score else b


def cluster_candidates(candidates: List[Candidate]) -> List[Candidate]:
    clusters: List[Dict[str, object]] = []
    for candidate in candidates:
        matched_cluster = None
        for cluster in clusters:
            rep = cluster["rep"]  # type: ignore[index]
            if hamming_distance_hex(candidate.phash, rep.phash) <= PHASH_DISTANCE_THRESHOLD:
                matched_cluster = cluster
                break

        if matched_cluster is None:
            cluster_id = f"cluster-{len(clusters)+1:03d}"
            candidate.phashClusterId = cluster_id
            clusters.append({"id": cluster_id, "rep": candidate, "items": [candidate], "best": candidate})
        else:
            cluster_id = matched_cluster["id"]  # type: ignore[index]
            candidate.phashClusterId = str(cluster_id)
            items = matched_cluster["items"]  # type: ignore[index]
            items.append(candidate)
            best = matched_cluster["best"]  # type: ignore[index]
            matched_cluster["best"] = choose_better(best, candidate)  # type: ignore[index]

    best_items: List[Candidate] = []
    for cluster in clusters:
        best = cluster["best"]  # type: ignore[index]
        best.phashClusterId = str(cluster["id"])  # type: ignore[index]
        best_items.append(best)
    return best_items


def polite_get(session: requests.Session, url: str, wait_s: float, last_request_ts: List[float]) -> Optional[requests.Response]:
    elapsed = time.time() - last_request_ts[0]
    if elapsed < wait_s:
        time.sleep(wait_s - elapsed)
    try:
        response = session.get(url, timeout=REQUEST_TIMEOUT)
        last_request_ts[0] = time.time()
        return response
    except requests.RequestException:
        last_request_ts[0] = time.time()
        return None


def download_image(session: requests.Session, url: str, wait_s: float, last_request_ts: List[float]) -> Optional[bytes]:
    for attempt in range(1, MAX_RETRIES + 1):
        response = polite_get(session, url, wait_s, last_request_ts)
        if response is None:
            time.sleep(0.35 * attempt)
            continue
        if response.status_code in (403, 404):
            return None
        if response.status_code >= 500:
            time.sleep(0.5 * attempt)
            continue
        if not response.ok:
            return None
        return response.content
    return None


def render_review_html(manifest: List[dict], output_dir: Path) -> None:
    serialized = json.dumps(manifest, indent=2)
    html = f"""<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>AGSA Candidate Image Review</title>
  <style>
    body {{ font-family: Inter, system-ui, sans-serif; margin: 20px; background: #f5f7fb; color: #10213a; }}
    .cluster {{ background: #fff; border: 1px solid #d9e1ee; border-radius: 12px; padding: 14px; margin-bottom: 14px; }}
    .row {{ display: grid; grid-template-columns: 220px 1fr; gap: 12px; align-items: start; }}
    img {{ width: 220px; aspect-ratio: 4/3; object-fit: cover; border-radius: 8px; border: 1px solid #d9e1ee; }}
    .meta p {{ margin: 2px 0; font-size: 13px; }}
    .toolbar {{ position: sticky; top: 0; background: #f5f7fb; padding: 8px 0; margin-bottom: 12px; }}
    button {{ border: 0; border-radius: 8px; padding: 10px 14px; background: #0e7490; color: #fff; cursor: pointer; }}
    textarea {{ width: 100%; min-height: 54px; }}
    .status {{ display: flex; gap: 10px; align-items: center; font-size: 13px; margin: 8px 0; }}
    code {{ background: #eef3fa; padding: 1px 4px; border-radius: 4px; }}
  </style>
</head>
<body>
  <h1>AGSA Candidate Image Review</h1>
  <p>Mark each image as approved, needs rights check, or reject. Then export <code>approved-manifest.json</code>.</p>
  <div class="toolbar">
    <button id="exportApproved">Export approved-manifest.json</button>
  </div>
  <div id="gallery"></div>
  <script>
    const data = {serialized};
    const gallery = document.getElementById("gallery");
    const state = Object.fromEntries(data.map((item) => [item.id, {{ status: "reject", notes: "" }}]));

    function render() {{
      gallery.innerHTML = "";
      for (const item of data) {{
        const card = document.createElement("article");
        card.className = "cluster";
        card.innerHTML = `
          <div class="row">
            <img src="./${{item.filename}}" alt="">
            <div class="meta">
              <p><strong>${{item.id}}</strong> | cluster: <code>${{item.phashClusterId}}</code></p>
              <p>${{item.width}}x${{item.height}} | ${{item.bytes}} bytes</p>
              <p><a href="${{item.sourcePage}}" target="_blank" rel="noreferrer">Source page</a></p>
              <p><a href="${{item.sourceImageUrl}}" target="_blank" rel="noreferrer">Source image URL</a></p>
              <p>Alt: ${{item.alt || "(none)"}}</p>
              <div class="status">
                <label><input type="radio" name="s-${{item.id}}" value="approved"> approved</label>
                <label><input type="radio" name="s-${{item.id}}" value="needs-rights-check"> needs rights check</label>
                <label><input type="radio" name="s-${{item.id}}" value="reject" checked> reject</label>
              </div>
              <label>Notes<br><textarea data-notes="${{item.id}}"></textarea></label>
            </div>
          </div>
        `;
        card.querySelectorAll(`input[name="s-${{item.id}}"]`).forEach((radio) => {{
          radio.addEventListener("change", (e) => state[item.id].status = e.target.value);
        }});
        card.querySelector(`[data-notes="${{item.id}}"]`).addEventListener("input", (e) => {{
          state[item.id].notes = e.target.value;
        }});
        gallery.appendChild(card);
      }}
    }}

    document.getElementById("exportApproved").addEventListener("click", () => {{
      const approved = data
        .filter((item) => state[item.id].status === "approved")
        .map((item) => ({{ ...item, reviewStatus: state[item.id].status, reviewNotes: state[item.id].notes }}));
      const blob = new Blob([JSON.stringify(approved, null, 2)], {{ type: "application/json" }});
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = "approved-manifest.json";
      a.click();
      URL.revokeObjectURL(url);
    }});
    render();
  </script>
</body>
</html>
"""
    (output_dir / "review.html").write_text(html, encoding="utf-8")


def main() -> int:
    args = parse_args()
    output_dir = Path(args.output_dir).resolve()
    output_dir.mkdir(parents=True, exist_ok=True)

    base_url = args.base_url.rstrip("/")
    base_parsed = urlparse(base_url)
    if base_parsed.scheme not in ("http", "https") or not base_parsed.hostname:
        print("Invalid --base-url")
        return 1
    base_hostname = base_parsed.hostname

    allowed_domains = {d.strip().lower() for d in args.allow_domains.split(",") if d.strip()}
    allowed_domains.add(base_hostname.lower())

    robots_url = f"{base_parsed.scheme}://{base_parsed.netloc}/robots.txt"
    rp = RobotFileParser()
    rp.set_url(robots_url)
    try:
        rp.read()
    except Exception:
        # Fail open on robots fetch failure, but still keep crawl bounded.
        pass

    session = requests.Session()
    session.headers.update({"User-Agent": USER_AGENT})
    wait_s = max(args.rate_limit_ms, 0) / 1000.0
    last_request_ts = [0.0]

    queue: deque[Tuple[str, int]] = deque([(canonical_page(base_url), 0)])
    visited_pages: Set[str] = set()
    downloaded_by_url: Dict[str, Candidate] = {}

    while queue and len(visited_pages) < args.max_pages:
        page_url, depth = queue.popleft()
        if page_url in visited_pages:
            continue
        if depth > args.max_depth:
            continue
        if not rp.can_fetch(USER_AGENT, page_url):
            continue

        response = polite_get(session, page_url, wait_s, last_request_ts)
        if response is None or not response.ok or "text/html" not in response.headers.get("Content-Type", ""):
            visited_pages.add(page_url)
            continue

        html = response.text
        visited_pages.add(page_url)
        print(f"[crawl] {page_url} (depth={depth})")

        for link in extract_links(html, page_url, base_hostname):
            if link not in visited_pages:
                queue.append((link, depth + 1))

        for image_url, alt in extract_image_urls(html, page_url):
            if image_url in downloaded_by_url:
                continue
            if not allowed_image_domain(image_url, allowed_domains):
                continue
            ext_hint = Path(urlparse(image_url).path).suffix.lower()
            if ext_hint and not IMG_EXT_RE.search(image_url) and ext_hint != ".svg":
                continue

            blob = download_image(session, image_url, wait_s, last_request_ts)
            if not blob:
                continue

            num_bytes = len(blob)
            try:
                image = Image.open(BytesIO(blob))
                image.load()
            except UnidentifiedImageError:
                continue
            except Exception:
                continue

            width, height = image.size
            ext = guess_extension(image_url, image)
            if should_skip(image_url, width, height, num_bytes, ext):
                continue

            phash = compute_phash(image)
            digest = hashlib.sha1(blob).hexdigest()[:16]
            filename = f"{digest}{ext}"
            out_path = output_dir / filename
            if not out_path.exists():
                out_path.write_bytes(blob)

            candidate = Candidate(
                id=digest,
                filename=filename,
                sourcePage=page_url,
                sourceImageUrl=image_url,
                alt=alt,
                width=width,
                height=height,
                bytes=num_bytes,
                phash=phash,
            )
            downloaded_by_url[image_url] = candidate
            print(f"  [img] {filename} {width}x{height} {num_bytes} bytes")

    deduped = cluster_candidates(list(downloaded_by_url.values()))

    keep_filenames = {item.filename for item in deduped}
    for path in output_dir.iterdir():
        if path.is_file() and path.suffix.lower() in {".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".tiff"}:
            if path.name not in keep_filenames:
                try:
                    path.unlink()
                except OSError:
                    pass

    manifest = [
        {
            "id": item.id,
            "filename": item.filename,
            "sourcePage": item.sourcePage,
            "sourceImageUrl": item.sourceImageUrl,
            "alt": item.alt,
            "width": item.width,
            "height": item.height,
            "bytes": item.bytes,
            "phashClusterId": item.phashClusterId,
        }
        for item in sorted(deduped, key=lambda x: (x.phashClusterId, -x.bytes))
    ]

    (output_dir / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
    approved_path = output_dir / "approved-manifest.json"
    if not approved_path.exists():
        approved_path.write_text("[]\n", encoding="utf-8")
    render_review_html(manifest, output_dir)

    print("")
    print(f"Crawl complete. Pages visited: {len(visited_pages)}")
    print(f"Candidates written: {len(manifest)}")
    print(f"Manifest: {output_dir / 'manifest.json'}")
    print(f"Review page: {output_dir / 'review.html'}")
    print("Human review is required before using any image in production.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
