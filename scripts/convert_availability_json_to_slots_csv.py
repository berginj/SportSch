#!/usr/bin/env python
"""Convert availability-by-date JSON to /api/import/slots CSV.

Usage:
  python scripts/convert_availability_json_to_slots_csv.py \
    --input docs/ponytail_availability_by_date_2026.json \
    --output docs/ponytail_availability_by_date_2026.csv \
    --offering-team-id LEAGUE_ADMIN
"""

import argparse
import csv
import json
import sys


def parse_args():
    parser = argparse.ArgumentParser(
        description="Convert availability JSON into importable slots CSV."
    )
    parser.add_argument("--input", required=True, help="Path to availability JSON file.")
    parser.add_argument("--output", help="Path to write CSV. Defaults to stdout.")
    parser.add_argument(
        "--offering-team-id",
        default="LEAGUE_ADMIN",
        help="OfferingTeamId value for each row.",
    )
    parser.add_argument("--game-type", default="Swap", help="GameType value.")
    parser.add_argument("--status", default="Open", help="Status value.")
    parser.add_argument("--notes", default="", help="Notes value for each row.")
    parser.add_argument(
        "--include-slot-type",
        action="store_true",
        help="Append slotType to notes if present.",
    )
    return parser.parse_args()


def main():
    args = parse_args()

    try:
        with open(args.input, "r", encoding="utf-8") as handle:
            data = json.load(handle)
    except (OSError, json.JSONDecodeError) as exc:
        print(f"Failed to read JSON: {exc}", file=sys.stderr)
        return 1

    if not isinstance(data, list):
        print("Expected top-level JSON array.", file=sys.stderr)
        return 1

    rows = []
    skipped = 0

    for day in data:
        date = (day or {}).get("date")
        fields = (day or {}).get("fields") or []
        if not date or not isinstance(fields, list):
            skipped += 1
            continue
        for field in fields:
            field_key = (field or {}).get("fieldKey")
            slots = (field or {}).get("slots") or []
            if not field_key or not isinstance(slots, list):
                skipped += 1
                continue
            for slot in slots:
                division = (slot or {}).get("division")
                start = (slot or {}).get("startTimeLocal")
                end = (slot or {}).get("endTimeLocal")
                slot_type = (slot or {}).get("slotType")

                if not (division and start and end):
                    skipped += 1
                    continue

                notes = args.notes or ""
                if args.include_slot_type and slot_type:
                    notes = f"{notes} | {slot_type}".strip(" |") if notes else str(slot_type)

                rows.append(
                    {
                        "division": division,
                        "offeringTeamId": args.offering_team_id,
                        "gameDate": date,
                        "startTime": start,
                        "endTime": end,
                        "fieldKey": field_key,
                        "gameType": args.game_type,
                        "status": args.status,
                        "notes": notes,
                    }
                )

    out_handle = sys.stdout
    close_handle = False
    if args.output:
        out_handle = open(args.output, "w", encoding="utf-8", newline="")
        close_handle = True

    writer = csv.DictWriter(
        out_handle,
        fieldnames=[
            "division",
            "offeringTeamId",
            "gameDate",
            "startTime",
            "endTime",
            "fieldKey",
            "gameType",
            "status",
            "notes",
        ],
    )
    writer.writeheader()
    writer.writerows(rows)

    if close_handle:
        out_handle.close()

    print(f"Wrote {len(rows)} rows. Skipped {skipped} items.", file=sys.stderr)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
