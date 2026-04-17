import { useEffect, useState } from "react";
import { apiFetch } from "../lib/api";

/**
 * Modal for requesting practice space directly from the calendar
 * Features:
 * - Pre-populated from calendar click (date, time)
 * - Real-time conflict detection
 * - Auto-approval messaging
 * - Simple 3-4 field form
 */
export default function PracticeRequestModal({
  isOpen,
  onClose,
  initialData = {},
  fields = [],
  me,
  onSuccess
}) {
  const [formData, setFormData] = useState({
    field: initialData.field || "",
    date: initialData.date || "",
    startTime: initialData.startTime || "",
    endTime: initialData.endTime || "",
    policy: "shared", // Default to shared
    notes: "",
    ...initialData
  });

  const [conflicts, setConflicts] = useState([]);
  const [checking, setChecking] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState("");

  // Auto-check conflicts when data changes
  useEffect(() => {
    if (!isOpen) return;
    if (!formData.field || !formData.date || !formData.startTime || !formData.endTime) return;

    const checkConflicts = async () => {
      setChecking(true);
      setError("");
      try {
        const result = await apiFetch("/api/practice/check-conflicts", {
          method: "POST",
          body: JSON.stringify({
            fieldKey: formData.field,
            date: formData.date,
            startTime: formData.startTime,
            endTime: formData.endTime,
            policy: formData.policy
          })
        });
        setConflicts(result.conflicts || []);
      } catch (err) {
        console.error("Conflict check failed:", err);
        setConflicts([]);
      } finally {
        setChecking(false);
      }
    };

    const debounced = setTimeout(checkConflicts, 300);
    return () => clearTimeout(debounced);
  }, [isOpen, formData.field, formData.date, formData.startTime, formData.endTime, formData.policy]);

  const handleSubmit = async (e) => {
    e.preventDefault();
    setSubmitting(true);
    setError("");

    try {
      const result = await apiFetch("/api/practice/requests", {
        method: "POST",
        body: JSON.stringify({
          fieldKey: formData.field,
          date: formData.date,
          startTime: formData.startTime,
          endTime: formData.endTime,
          policy: formData.policy,
          notes: formData.notes
        })
      });

      onSuccess(result);
      onClose();
    } catch (err) {
      setError(err.message || "Failed to create practice request");
    } finally {
      setSubmitting(false);
    }
  };

  const handleChange = (field, value) => {
    setFormData(prev => ({ ...prev, [field]: value }));
  };

  const canAutoApprove = conflicts.length === 0 ||
    (formData.policy === "shared" && conflicts.every(c => c.policy === "shared"));

  if (!isOpen) return null;

  return (
    <div
      className="fixed inset-0 bg-black/50 flex items-center justify-center z-50"
      onClick={(e) => e.target === e.currentTarget && onClose()}
    >
      <div className="bg-white dark:bg-gray-800 rounded-lg shadow-xl max-w-md w-full mx-4 max-h-[90vh] overflow-y-auto">
        {/* Header */}
        <div className="border-b border-gray-200 dark:border-gray-700 px-6 py-4">
          <h2 className="text-xl font-semibold text-gray-900 dark:text-white">
            Request Practice Space
          </h2>
          <p className="text-sm text-gray-600 dark:text-gray-400 mt-1">
            Reserve a field for practice
          </p>
        </div>

        {/* Form */}
        <form onSubmit={handleSubmit} className="px-6 py-4 space-y-4">
          {/* Field Selection */}
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
              Field *
            </label>
            <select
              required
              value={formData.field}
              onChange={(e) => handleChange("field", e.target.value)}
              className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md
                bg-white dark:bg-gray-700 text-gray-900 dark:text-white
                focus:ring-2 focus:ring-blue-500 focus:border-blue-500"
            >
              <option value="">Select a field...</option>
              {fields.map((field) => (
                <option key={field.fieldKey} value={field.fieldKey}>
                  {field.displayName || field.fieldName || field.fieldKey}
                  {field.parkName && ` - ${field.parkName}`}
                </option>
              ))}
            </select>
          </div>

          {/* Date */}
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
              Date *
            </label>
            <input
              type="date"
              required
              value={formData.date}
              onChange={(e) => handleChange("date", e.target.value)}
              className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md
                bg-white dark:bg-gray-700 text-gray-900 dark:text-white
                focus:ring-2 focus:ring-blue-500 focus:border-blue-500"
            />
          </div>

          {/* Time Range */}
          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
                Start Time *
              </label>
              <input
                type="time"
                required
                value={formData.startTime}
                onChange={(e) => handleChange("startTime", e.target.value)}
                className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md
                  bg-white dark:bg-gray-700 text-gray-900 dark:text-white
                  focus:ring-2 focus:ring-blue-500 focus:border-blue-500"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
                End Time *
              </label>
              <input
                type="time"
                required
                value={formData.endTime}
                onChange={(e) => handleChange("endTime", e.target.value)}
                className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md
                  bg-white dark:bg-gray-700 text-gray-900 dark:text-white
                  focus:ring-2 focus:ring-blue-500 focus:border-blue-500"
              />
            </div>
          </div>

          {/* Booking Policy */}
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">
              Booking Type *
            </label>
            <div className="space-y-2">
              <label className="flex items-start cursor-pointer">
                <input
                  type="radio"
                  name="policy"
                  value="shared"
                  checked={formData.policy === "shared"}
                  onChange={(e) => handleChange("policy", e.target.value)}
                  className="mt-1 mr-3"
                />
                <div>
                  <div className="font-medium text-gray-900 dark:text-white">
                    Shared
                  </div>
                  <div className="text-sm text-gray-600 dark:text-gray-400">
                    OK if other teams practice at the same time
                  </div>
                </div>
              </label>
              <label className="flex items-start cursor-pointer">
                <input
                  type="radio"
                  name="policy"
                  value="exclusive"
                  checked={formData.policy === "exclusive"}
                  onChange={(e) => handleChange("policy", e.target.value)}
                  className="mt-1 mr-3"
                />
                <div>
                  <div className="font-medium text-gray-900 dark:text-white">
                    Exclusive
                  </div>
                  <div className="text-sm text-gray-600 dark:text-gray-400">
                    We need the entire field
                  </div>
                </div>
              </label>
            </div>
          </div>

          {/* Notes (Optional) */}
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
              Notes (Optional)
            </label>
            <textarea
              value={formData.notes}
              onChange={(e) => handleChange("notes", e.target.value)}
              rows={2}
              placeholder="Any additional information..."
              className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md
                bg-white dark:bg-gray-700 text-gray-900 dark:text-white
                focus:ring-2 focus:ring-blue-500 focus:border-blue-500"
            />
          </div>

          {/* Conflict Detection Status */}
          {checking && (
            <div className="flex items-center gap-2 p-3 bg-blue-50 dark:bg-blue-900/20 border border-blue-200 dark:border-blue-800 rounded-md">
              <svg className="animate-spin h-5 w-5 text-blue-600" viewBox="0 0 24 24">
                <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" fill="none" />
                <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
              </svg>
              <span className="text-sm text-blue-800 dark:text-blue-200">
                Checking for conflicts...
              </span>
            </div>
          )}

          {/* Conflicts Warning */}
          {!checking && conflicts.length > 0 && (
            <div className="p-3 bg-yellow-50 dark:bg-yellow-900/20 border border-yellow-200 dark:border-yellow-800 rounded-md">
              <div className="flex items-start">
                <svg className="h-5 w-5 text-yellow-600 mr-2 mt-0.5" fill="currentColor" viewBox="0 0 20 20">
                  <path fillRule="evenodd" d="M8.257 3.099c.765-1.36 2.722-1.36 3.486 0l5.58 9.92c.75 1.334-.213 2.98-1.742 2.98H4.42c-1.53 0-2.493-1.646-1.743-2.98l5.58-9.92zM11 13a1 1 0 11-2 0 1 1 0 012 0zm-1-8a1 1 0 00-1 1v3a1 1 0 002 0V6a1 1 0 00-1-1z" clipRule="evenodd" />
                </svg>
                <div className="flex-1">
                  <h4 className="text-sm font-medium text-yellow-800 dark:text-yellow-200">
                    Conflicts Detected
                  </h4>
                  <ul className="mt-2 text-sm text-yellow-700 dark:text-yellow-300 space-y-1">
                    {conflicts.map((conflict, idx) => (
                      <li key={idx}>
                        {conflict.teamName || "Another team"}: {conflict.startTime} - {conflict.endTime}
                        {conflict.policy === "exclusive" && " (Exclusive)"}
                      </li>
                    ))}
                  </ul>
                  <p className="mt-2 text-sm text-yellow-700 dark:text-yellow-300">
                    Request will require admin approval.
                  </p>
                </div>
              </div>
            </div>
          )}

          {/* Success Message */}
          {!checking && canAutoApprove && formData.field && formData.date && (
            <div className="p-3 bg-green-50 dark:bg-green-900/20 border border-green-200 dark:border-green-800 rounded-md">
              <div className="flex items-start">
                <svg className="h-5 w-5 text-green-600 mr-2 mt-0.5" fill="currentColor" viewBox="0 0 20 20">
                  <path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.707-9.293a1 1 0 00-1.414-1.414L9 10.586 7.707 9.293a1 1 0 00-1.414 1.414l2 2a1 1 0 001.414 0l4-4z" clipRule="evenodd" />
                </svg>
                <div>
                  <h4 className="text-sm font-medium text-green-800 dark:text-green-200">
                    No conflicts!
                  </h4>
                  <p className="text-sm text-green-700 dark:text-green-300 mt-1">
                    This request will be auto-approved and appear on your calendar immediately.
                  </p>
                </div>
              </div>
            </div>
          )}

          {/* Error Message */}
          {error && (
            <div className="p-3 bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 rounded-md">
              <p className="text-sm text-red-800 dark:text-red-200">
                {error}
              </p>
            </div>
          )}
        </form>

        {/* Footer */}
        <div className="border-t border-gray-200 dark:border-gray-700 px-6 py-4 flex justify-end gap-3">
          <button
            type="button"
            onClick={onClose}
            disabled={submitting}
            className="px-4 py-2 text-sm font-medium text-gray-700 dark:text-gray-300
              bg-white dark:bg-gray-700 border border-gray-300 dark:border-gray-600
              rounded-md hover:bg-gray-50 dark:hover:bg-gray-600
              disabled:opacity-50 disabled:cursor-not-allowed"
          >
            Cancel
          </button>
          <button
            type="submit"
            onClick={handleSubmit}
            disabled={submitting || checking || !formData.field || !formData.date || !formData.startTime || !formData.endTime}
            className="px-4 py-2 text-sm font-medium text-white
              bg-blue-600 hover:bg-blue-700 rounded-md
              disabled:opacity-50 disabled:cursor-not-allowed
              flex items-center gap-2"
          >
            {submitting && (
              <svg className="animate-spin h-4 w-4" viewBox="0 0 24 24">
                <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" fill="none" />
                <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
              </svg>
            )}
            {canAutoApprove ? "Confirm Practice" : "Submit Request"}
          </button>
        </div>
      </div>
    </div>
  );
}
