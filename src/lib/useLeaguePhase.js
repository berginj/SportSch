import { useMemo } from 'react';

/**
 * useLeaguePhase - Determines if league is in setup phase or active season
 *
 * Helps make smart UI decisions about which sections should be expanded/collapsed by default
 *
 * Returns:
 * - phase: 'setup' | 'active' | 'complete'
 * - isSetup: boolean - true if in setup phase
 * - isActive: boolean - true if in active season
 * - isComplete: boolean - true if season is over
 * - setupProgress: number (0-100) - rough estimate of setup completion
 */
export function useLeaguePhase(leagueData) {
  return useMemo(() => {
    if (!leagueData) {
      return {
        phase: 'setup',
        isSetup: true,
        isActive: false,
        isComplete: false,
        setupProgress: 0
      };
    }

    // Check for season dates
    const seasonStart = leagueData.seasonStart ? new Date(leagueData.seasonStart) : null;
    const seasonEnd = leagueData.seasonEnd ? new Date(leagueData.seasonEnd) : null;
    const now = new Date();

    // Determine phase based on dates
    let phase = 'setup';
    if (seasonStart && seasonEnd) {
      if (now >= seasonStart && now <= seasonEnd) {
        phase = 'active';
      } else if (now > seasonEnd) {
        phase = 'complete';
      }
    }

    // Calculate rough setup progress (0-100)
    const checks = {
      hasDivisions: !!leagueData.divisionCount || !!leagueData.divisions?.length,
      hasTeams: !!leagueData.teamCount || !!leagueData.teams?.length,
      hasFields: !!leagueData.fieldCount || !!leagueData.fields?.length,
      hasSeasonDates: !!(seasonStart && seasonEnd),
      hasGameLength: !!leagueData.gameLength,
      hasSchedule: !!leagueData.gameCount || !!leagueData.hasScheduledGames
    };

    const completed = Object.values(checks).filter(Boolean).length;
    const total = Object.keys(checks).length;
    const setupProgress = Math.round((completed / total) * 100);

    return {
      phase,
      isSetup: phase === 'setup',
      isActive: phase === 'active',
      isComplete: phase === 'complete',
      setupProgress
    };
  }, [leagueData]);
}

/**
 * useFeatureDisclosure - Smart defaults for which sections should be expanded
 *
 * Returns an object with feature names as keys and boolean (should expand) as values
 */
export function useFeatureDisclosure(leaguePhase) {
  return useMemo(() => {
    const { isSetup, isActive, setupProgress } = leaguePhase;

    return {
      // Setup phase features - expanded during setup, collapsed in active season
      divisions: isSetup && setupProgress < 50,
      teams: isSetup && setupProgress < 50,
      fields: isSetup && setupProgress < 50,
      leagueSettings: isSetup && setupProgress < 30,
      divisionOverrides: false, // Rare, always collapsed
      fieldBlackouts: false, // Occasional, collapsed by default

      // Active season features - collapsed during setup, expanded when active
      scheduler: isActive || setupProgress > 70,
      practiceRequests: isActive,
      coachLinks: isSetup && setupProgress > 50 && setupProgress < 80,

      // Advanced features - always collapsed
      backups: false,
      seasonReset: false,
      availabilityInsights: false,
      slotGenerator: false,

      // Admin features - always collapsed
      csvImport: false,
      globalAdmin: false
    };
  }, [leaguePhase]);
}
