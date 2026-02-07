const boardMembers = [
  { role: "President", name: "Mitch McCann", email: "agsafastpitchpresident@gmail.com" },
  { role: "Treasurer", name: "Daryn Larkin", email: "agsafastpitchtreasurer@gmail.com" },
  { role: "Secretary", name: "Nancy Dotterweich", email: "agsafastpitchsecretary@gmail.com" },
  { role: "Travel Team Director", name: "Heather Grady", email: "agsafastpitchtravel@gmail.com" },
];

const quickLinks = [
  { label: "Spring/Summer Calendar", href: "https://www.agsafastpitch.com/Calendar/" },
  { label: "Fall Ball Calendar", href: "https://www.agsafastpitch.com/Calendar/" },
  { label: "Weather Hotline", href: "tel:9082790303", note: "(908) 279-0303" },
  { label: "Registration", href: "https://www.agsafastpitch.com/Registration/" },
];

const divisions = [
  "6U Tee Ball",
  "8U",
  "10U",
  "12U",
  "14U",
  "16/18U",
];

const seasonMilestones = [
  "April: Opening Day parade and kickoff",
  "April to June: Regular season + travel tournaments",
  "June: Playoffs and championship weekend",
];

export default function AgsaSitePage() {
  return (
    <div
      style={{
        minHeight: "100vh",
        background: "linear-gradient(180deg, #f7fafc 0%, #eef5ff 45%, #ffffff 100%)",
        color: "#132033",
      }}
    >
      <header
        style={{
          borderBottom: "1px solid #dbe6f3",
          background: "rgba(255,255,255,0.88)",
          backdropFilter: "blur(8px)",
          position: "sticky",
          top: 0,
          zIndex: 10,
        }}
      >
        <div style={{ maxWidth: 1100, margin: "0 auto", padding: "14px 20px", display: "flex", alignItems: "center", justifyContent: "space-between", gap: 12 }}>
          <div style={{ fontWeight: 800, letterSpacing: "0.02em" }}>AGSA FASTPITCH</div>
          <nav style={{ display: "flex", gap: 16, fontSize: 14, color: "#30445e", flexWrap: "wrap" }}>
            <a href="#about">About</a>
            <a href="#divisions">Divisions</a>
            <a href="#calendar">Calendar</a>
            <a href="#board">Board</a>
            <a href="/app" style={{ fontWeight: 700 }}>League App</a>
          </nav>
        </div>
      </header>

      <main style={{ maxWidth: 1100, margin: "0 auto", padding: "28px 20px 56px" }}>
        <section style={{ display: "grid", gridTemplateColumns: "1.2fr 1fr", gap: 20 }}>
          <article style={{ background: "#ffffff", border: "1px solid #dbe6f3", borderRadius: 16, padding: 24 }}>
            <p style={{ margin: 0, fontSize: 12, fontWeight: 700, color: "#0a66c2", letterSpacing: "0.08em" }}>MIDDLESEX COUNTY FASTPITCH</p>
            <h1 style={{ margin: "10px 0 12px", fontSize: 38, lineHeight: 1.08 }}>
              Welcome to AGSA Softball
            </h1>
            <p style={{ margin: 0, color: "#344a65", fontSize: 16, lineHeight: 1.6 }}>
              The Avenel Green Street Association has provided girls fastpitch softball for players ages 4 to 18 since 1974. We run recreational and competitive travel softball, focused on skill development, teamwork, and sportsmanship.
            </p>
            <div style={{ marginTop: 18, display: "flex", gap: 10, flexWrap: "wrap" }}>
              <a href="https://www.agsafastpitch.com/Registration/" style={{ textDecoration: "none", background: "#0a66c2", color: "#fff", padding: "11px 16px", borderRadius: 10, fontWeight: 700 }}>Register</a>
              <a href="https://www.agsafastpitch.com/Calendar/" style={{ textDecoration: "none", background: "#e7f1fc", color: "#0a66c2", padding: "11px 16px", borderRadius: 10, fontWeight: 700 }}>View Calendar</a>
            </div>
          </article>

          <aside style={{ background: "#0f2137", color: "#f0f6ff", borderRadius: 16, padding: 22 }}>
            <h2 style={{ marginTop: 0, marginBottom: 10, fontSize: 20 }}>Quick Links</h2>
            <ul style={{ margin: 0, paddingLeft: 18, lineHeight: 1.9 }}>
              {quickLinks.map((item) => (
                <li key={item.label}>
                  <a href={item.href} style={{ color: "#b8ddff" }}>{item.label}</a>
                  {item.note ? ` ${item.note}` : ""}
                </li>
              ))}
            </ul>
          </aside>
        </section>

        <section id="about" style={{ marginTop: 20, display: "grid", gridTemplateColumns: "1fr 1fr", gap: 20 }}>
          <article style={{ background: "#fff", border: "1px solid #dbe6f3", borderRadius: 16, padding: 22 }}>
            <h2 style={{ marginTop: 0 }}>About AGSA</h2>
            <p style={{ color: "#344a65", lineHeight: 1.7, marginBottom: 0 }}>
              AGSA started in 1974 as a three-team league and has grown into one of the area&apos;s largest and most respected fastpitch programs. Our mission is to provide a safe, positive environment where girls can learn softball and life skills.
            </p>
          </article>
          <article id="divisions" style={{ background: "#fff", border: "1px solid #dbe6f3", borderRadius: 16, padding: 22 }}>
            <h2 style={{ marginTop: 0 }}>Age Divisions</h2>
            <ul style={{ margin: 0, paddingLeft: 18, lineHeight: 1.85, color: "#344a65" }}>
              {divisions.map((d) => <li key={d}>{d}</li>)}
            </ul>
          </article>
        </section>

        <section id="calendar" style={{ marginTop: 20, background: "#fff", border: "1px solid #dbe6f3", borderRadius: 16, padding: 22 }}>
          <h2 style={{ marginTop: 0 }}>Season Timeline</h2>
          <ul style={{ margin: 0, paddingLeft: 18, lineHeight: 1.9, color: "#344a65" }}>
            {seasonMilestones.map((m) => <li key={m}>{m}</li>)}
          </ul>
        </section>

        <section id="board" style={{ marginTop: 20, background: "#fff", border: "1px solid #dbe6f3", borderRadius: 16, padding: 22 }}>
          <h2 style={{ marginTop: 0 }}>Board Contacts</h2>
          <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(220px, 1fr))", gap: 12 }}>
            {boardMembers.map((b) => (
              <div key={b.role} style={{ border: "1px solid #dbe6f3", borderRadius: 12, padding: 14 }}>
                <div style={{ fontSize: 12, letterSpacing: "0.05em", color: "#53708f", textTransform: "uppercase", fontWeight: 700 }}>{b.role}</div>
                <div style={{ marginTop: 6, fontWeight: 700 }}>{b.name}</div>
                <a href={`mailto:${b.email}`} style={{ marginTop: 4, display: "inline-block", color: "#0a66c2" }}>{b.email}</a>
              </div>
            ))}
          </div>
        </section>
      </main>
    </div>
  );
}
