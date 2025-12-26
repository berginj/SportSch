export default function StatusCard({ title, message, tone = "info", children }) {
  const toneClass = tone === "error" ? "statusCard statusCard--error" : "statusCard";
  return (
    <div className={`card ${toneClass}`}>
      {title ? <div className="statusCard__title">{title}</div> : null}
      {message ? <div className="statusCard__message">{message}</div> : null}
      {children}
    </div>
  );
}
