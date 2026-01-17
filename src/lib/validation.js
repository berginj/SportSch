/**
 * Form validation utilities with real-time feedback
 */

/**
 * Validation rules
 */
export const validators = {
  required: (value, fieldName = 'This field') => {
    if (!value || (typeof value === 'string' && !value.trim())) {
      return `${fieldName} is required`;
    }
    return null;
  },

  email: (value) => {
    if (!value) return null;
    const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
    if (!emailRegex.test(value)) {
      return 'Please enter a valid email address';
    }
    return null;
  },

  minLength: (min) => (value, fieldName = 'This field') => {
    if (!value) return null;
    if (value.length < min) {
      return `${fieldName} must be at least ${min} characters`;
    }
    return null;
  },

  maxLength: (max) => (value, fieldName = 'This field') => {
    if (!value) return null;
    if (value.length > max) {
      return `${fieldName} must be no more than ${max} characters`;
    }
    return null;
  },

  pattern: (regex, message = 'Invalid format') => (value) => {
    if (!value) return null;
    if (!regex.test(value)) {
      return message;
    }
    return null;
  },

  number: (value, fieldName = 'This field') => {
    if (!value) return null;
    if (isNaN(Number(value))) {
      return `${fieldName} must be a number`;
    }
    return null;
  },

  min: (min) => (value, fieldName = 'This field') => {
    if (!value) return null;
    if (Number(value) < min) {
      return `${fieldName} must be at least ${min}`;
    }
    return null;
  },

  max: (max) => (value, fieldName = 'This field') => {
    if (!value) return null;
    if (Number(value) > max) {
      return `${fieldName} must be no more than ${max}`;
    }
    return null;
  },

  date: (value) => {
    if (!value) return null;
    const dateRegex = /^\d{4}-\d{2}-\d{2}$/;
    if (!dateRegex.test(value)) {
      return 'Date must be in YYYY-MM-DD format';
    }
    const date = new Date(value);
    if (isNaN(date.getTime())) {
      return 'Please enter a valid date';
    }
    return null;
  },

  time: (value) => {
    if (!value) return null;
    const timeRegex = /^([01]\d|2[0-3]):([0-5]\d)$/;
    if (!timeRegex.test(value)) {
      return 'Time must be in HH:MM format (24-hour)';
    }
    return null;
  },

  custom: (validatorFn, errorMessage) => (value) => {
    if (!value) return null;
    const isValid = validatorFn(value);
    return isValid ? null : errorMessage;
  },
};

/**
 * Validate a single field with multiple rules
 */
export function validateField(value, rules, fieldName) {
  for (const rule of rules) {
    const error = rule(value, fieldName);
    if (error) return error;
  }
  return null;
}

/**
 * Validate an entire form
 */
export function validateForm(values, schema) {
  const errors = {};
  let hasErrors = false;

  for (const [fieldName, rules] of Object.entries(schema)) {
    const value = values[fieldName];
    const fieldLabel = fieldName.charAt(0).toUpperCase() + fieldName.slice(1);
    const error = validateField(value, rules, fieldLabel);

    if (error) {
      errors[fieldName] = error;
      hasErrors = true;
    }
  }

  return { errors, isValid: !hasErrors };
}

/**
 * Common validation schemas
 */
export const schemas = {
  accessRequest: {
    requestedRole: [validators.required],
  },

  league: {
    leagueId: [
      validators.required,
      validators.minLength(2),
      validators.maxLength(10),
      validators.pattern(/^[A-Z0-9]+$/, 'League ID must be uppercase letters and numbers only'),
    ],
    name: [validators.required, validators.minLength(2), validators.maxLength(100)],
  },

  division: {
    code: [
      validators.required,
      validators.minLength(2),
      validators.maxLength(20),
    ],
    name: [validators.minLength(2), validators.maxLength(100)],
  },

  team: {
    teamId: [validators.required, validators.minLength(2), validators.maxLength(50)],
    name: [validators.required, validators.minLength(2), validators.maxLength(100)],
  },

  field: {
    parkCode: [validators.required, validators.minLength(2), validators.maxLength(50)],
    fieldCode: [validators.required, validators.minLength(1), validators.maxLength(20)],
    parkName: [validators.minLength(2), validators.maxLength(100)],
    fieldName: [validators.minLength(1), validators.maxLength(100)],
  },

  slot: {
    gameDate: [validators.required, validators.date],
    startTime: [validators.required, validators.time],
    endTime: [validators.required, validators.time],
  },

  seasonConfig: {
    gameLengthMinutes: [validators.required, validators.number, validators.min(30), validators.max(300)],
  },
};
