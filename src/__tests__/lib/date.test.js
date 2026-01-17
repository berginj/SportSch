import { describe, it, expect } from 'vitest';
import { isStrictIsoDate, validateIsoDates } from '../../lib/date';

describe('isStrictIsoDate', () => {
  it('should validate correct YYYY-MM-DD format', () => {
    expect(isStrictIsoDate('2026-01-16')).toBe(true);
    expect(isStrictIsoDate('2026-12-31')).toBe(true);
    expect(isStrictIsoDate('2026-06-15')).toBe(true);
  });

  it('should reject single-digit months or days', () => {
    expect(isStrictIsoDate('2026-1-16')).toBe(false);
    expect(isStrictIsoDate('2026-01-6')).toBe(false);
    expect(isStrictIsoDate('2026-1-6')).toBe(false);
  });

  it('should reject non-ISO formats', () => {
    expect(isStrictIsoDate('01/16/2026')).toBe(false);
    expect(isStrictIsoDate('16-01-2026')).toBe(false);
    expect(isStrictIsoDate('Jan 16, 2026')).toBe(false);
  });

  it('should reject invalid dates', () => {
    expect(isStrictIsoDate('2026-02-30')).toBe(false); // Feb 30 doesn't exist
    expect(isStrictIsoDate('2026-13-01')).toBe(false); // Month 13 doesn't exist
    expect(isStrictIsoDate('2026-00-15')).toBe(false); // Month 0 doesn't exist
  });

  it('should handle empty or invalid input', () => {
    expect(isStrictIsoDate('')).toBe(false);
    expect(isStrictIsoDate(null)).toBe(false);
    expect(isStrictIsoDate(undefined)).toBe(false);
    expect(isStrictIsoDate('not-a-date')).toBe(false);
  });

  it('should trim whitespace', () => {
    expect(isStrictIsoDate('  2026-01-16  ')).toBe(true);
  });

  it('should validate leap years correctly', () => {
    expect(isStrictIsoDate('2024-02-29')).toBe(true); // 2024 is a leap year
    expect(isStrictIsoDate('2025-02-29')).toBe(false); // 2025 is not a leap year
  });
});

describe('validateIsoDates', () => {
  it('should return empty string for valid dates', () => {
    const fields = [
      { label: 'Start Date', value: '2026-01-16', required: true },
      { label: 'End Date', value: '2026-12-31', required: true }
    ];
    expect(validateIsoDates(fields)).toBe('');
  });

  it('should return error for required empty field', () => {
    const fields = [
      { label: 'Start Date', value: '', required: true }
    ];
    expect(validateIsoDates(fields)).toBe('Start Date is required.');
  });

  it('should allow optional empty fields', () => {
    const fields = [
      { label: 'Start Date', value: '2026-01-16', required: true },
      { label: 'End Date', value: '', required: false }
    ];
    expect(validateIsoDates(fields)).toBe('');
  });

  it('should return error for invalid date format', () => {
    const fields = [
      { label: 'Start Date', value: '01/16/2026', required: true }
    ];
    expect(validateIsoDates(fields)).toBe('Start Date must be YYYY-MM-DD.');
  });

  it('should return error for first invalid field', () => {
    const fields = [
      { label: 'Start Date', value: '2026-01-16', required: true },
      { label: 'End Date', value: 'invalid', required: true },
      { label: 'Other Date', value: 'also-invalid', required: true }
    ];
    expect(validateIsoDates(fields)).toBe('End Date must be YYYY-MM-DD.');
  });

  it('should use default label "Date" if label not provided', () => {
    const fields = [
      { value: '', required: true }
    ];
    expect(validateIsoDates(fields)).toBe('Date is required.');
  });

  it('should handle empty array', () => {
    expect(validateIsoDates([])).toBe('');
  });

  it('should handle null/undefined input', () => {
    expect(validateIsoDates(null)).toBe('');
    expect(validateIsoDates(undefined)).toBe('');
  });

  it('should trim whitespace from values', () => {
    const fields = [
      { label: 'Start Date', value: '  2026-01-16  ', required: true }
    ];
    expect(validateIsoDates(fields)).toBe('');
  });
});
