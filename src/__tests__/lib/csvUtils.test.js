import { describe, it, expect } from 'vitest';
import { csvEscape, buildTeamsTemplateCsv, downloadCsv } from '../../lib/csvUtils';

describe('csvUtils', () => {
  describe('csvEscape', () => {
    it('should return plain values unchanged', () => {
      expect(csvEscape('hello')).toBe('hello');
      expect(csvEscape('123')).toBe('123');
    });

    it('should wrap values with commas in quotes', () => {
      expect(csvEscape('hello, world')).toBe('"hello, world"');
    });

    it('should wrap values with quotes in quotes and escape inner quotes', () => {
      expect(csvEscape('say "hello"')).toBe('"say ""hello"""');
    });

    it('should wrap values with newlines in quotes', () => {
      expect(csvEscape('line1\nline2')).toBe('"line1\nline2"');
    });

    it('should handle null and undefined', () => {
      expect(csvEscape(null)).toBe('');
      expect(csvEscape(undefined)).toBe('');
    });
  });

  describe('buildTeamsTemplateCsv', () => {
    it('should generate CSV with header', () => {
      const csv = buildTeamsTemplateCsv([]);
      expect(csv).toBe('division,teamId,name,coachName,coachEmail,coachPhone');
    });

    it('should generate rows for division strings', () => {
      const csv = buildTeamsTemplateCsv(['U8', 'U10']);
      const lines = csv.split('\n');
      expect(lines).toHaveLength(3);
      expect(lines[0]).toBe('division,teamId,name,coachName,coachEmail,coachPhone');
      expect(lines[1]).toBe('U8,,,,,');
      expect(lines[2]).toBe('U10,,,,,');
    });

    it('should generate rows for division objects', () => {
      const divisions = [
        { code: 'U8', isActive: true },
        { division: 'U10', isActive: true },
      ];
      const csv = buildTeamsTemplateCsv(divisions);
      const lines = csv.split('\n');
      expect(lines).toHaveLength(3);
      expect(lines[1]).toBe('U8,,,,,');
      expect(lines[2]).toBe('U10,,,,,');
    });

    it('should filter out inactive divisions', () => {
      const divisions = [
        { code: 'U8', isActive: true },
        { code: 'U10', isActive: false },
        { code: 'U12', isActive: true },
      ];
      const csv = buildTeamsTemplateCsv(divisions);
      const lines = csv.split('\n');
      expect(lines).toHaveLength(3); // header + 2 active divisions
      expect(csv).not.toContain('U10');
    });

    it('should filter out null/undefined divisions', () => {
      const divisions = ['U8', null, undefined, 'U10'];
      const csv = buildTeamsTemplateCsv(divisions);
      const lines = csv.split('\n');
      expect(lines).toHaveLength(3); // header + 2 valid divisions
    });
  });

  describe('downloadCsv', () => {
    it('should create a blob and trigger download', () => {
      // Mock URL.createObjectURL and revokeObjectURL
      const mockUrl = 'blob:mock-url';
      global.URL.createObjectURL = vi.fn(() => mockUrl);
      global.URL.revokeObjectURL = vi.fn();

      // Mock document.createElement and appendChild
      const mockLink = {
        href: '',
        download: '',
        click: vi.fn(),
        remove: vi.fn(),
        setAttribute: vi.fn((attr, value) => {
          mockLink[attr] = value;
        }),
      };
      vi.spyOn(document, 'createElement').mockReturnValue(mockLink);
      vi.spyOn(document.body, 'appendChild').mockImplementation(() => {});

      downloadCsv('test,data', 'test.csv');

      expect(global.URL.createObjectURL).toHaveBeenCalled();
      expect(mockLink.href).toBe(mockUrl);
      expect(mockLink.download).toBe('test.csv');
      expect(mockLink.click).toHaveBeenCalled();
      expect(mockLink.remove).toHaveBeenCalled();
      expect(global.URL.revokeObjectURL).toHaveBeenCalledWith(mockUrl);
    });
  });
});
