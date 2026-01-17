namespace GameSwap.Functions.Storage;

/// <summary>
/// Centralized error codes for API responses.
/// These codes provide structured error handling across all endpoints.
/// </summary>
public static class ErrorCodes
{
    // Authentication & Authorization
    public const string UNAUTHENTICATED = "UNAUTHENTICATED";
    public const string UNAUTHORIZED = "UNAUTHORIZED";
    public const string FORBIDDEN = "FORBIDDEN";

    // Resource Not Found
    public const string NOT_FOUND = "NOT_FOUND";
    public const string FIELD_NOT_FOUND = "FIELD_NOT_FOUND";
    public const string SLOT_NOT_FOUND = "SLOT_NOT_FOUND";
    public const string TEAM_NOT_FOUND = "TEAM_NOT_FOUND";
    public const string DIVISION_NOT_FOUND = "DIVISION_NOT_FOUND";
    public const string LEAGUE_NOT_FOUND = "LEAGUE_NOT_FOUND";
    public const string REQUEST_NOT_FOUND = "REQUEST_NOT_FOUND";
    public const string RULE_NOT_FOUND = "RULE_NOT_FOUND";

    // Validation Errors
    public const string BAD_REQUEST = "BAD_REQUEST";
    public const string INVALID_DATE = "INVALID_DATE";
    public const string INVALID_TIME = "INVALID_TIME";
    public const string INVALID_DATE_RANGE = "INVALID_DATE_RANGE";
    public const string INVALID_TIME_RANGE = "INVALID_TIME_RANGE";
    public const string INVALID_FIELD_KEY = "INVALID_FIELD_KEY";
    public const string INVALID_TABLE_KEY = "INVALID_TABLE_KEY";
    public const string MISSING_REQUIRED_FIELD = "MISSING_REQUIRED_FIELD";

    // Business Logic Errors
    public const string SLOT_CONFLICT = "SLOT_CONFLICT";
    public const string FIELD_IN_USE = "FIELD_IN_USE";
    public const string COACH_TEAM_REQUIRED = "COACH_TEAM_REQUIRED";
    public const string COACH_DIVISION_MISMATCH = "COACH_DIVISION_MISMATCH";
    public const string ALREADY_EXISTS = "ALREADY_EXISTS";
    public const string INVALID_STATUS_TRANSITION = "INVALID_STATUS_TRANSITION";
    public const string REQUEST_ALREADY_APPROVED = "REQUEST_ALREADY_APPROVED";
    public const string CANNOT_APPROVE_OWN_REQUEST = "CANNOT_APPROVE_OWN_REQUEST";
    public const string SLOT_NOT_AVAILABLE = "SLOT_NOT_AVAILABLE";

    // Conflicts
    public const string CONFLICT = "CONFLICT";
    public const string CONCURRENT_MODIFICATION = "CONCURRENT_MODIFICATION";

    // Server Errors
    public const string INTERNAL_ERROR = "INTERNAL_ERROR";
    public const string SERVICE_UNAVAILABLE = "SERVICE_UNAVAILABLE";
}
