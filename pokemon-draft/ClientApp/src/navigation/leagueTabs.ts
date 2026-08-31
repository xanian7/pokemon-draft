export type LeagueTab =
  | 'home'
  | 'team'
  | 'manage'
  | 'activity'
  | 'matchup'
  | 'teams'
  | 'schedule'
  | 'stats'
  | 'playoffs'
  | 'draft'
  | 'setup'
  | 'pokemon'

export interface LeagueTabDefinition {
  label: string
  value: LeagueTab
  icon: string
  adminOnly?: boolean
}

export const leagueTabs: LeagueTabDefinition[] = [
  { label: 'Home', value: 'home', icon: 'mdi-home' },
  { label: 'My Team', value: 'team', icon: 'mdi-account' },
  { label: 'Manage Team', value: 'manage', icon: 'mdi-account-edit' },
  { label: 'Activity', value: 'activity', icon: 'mdi-history' },
  { label: 'Matchup', value: 'matchup', icon: 'mdi-sword-cross' },
  { label: 'All Teams', value: 'teams', icon: 'mdi-account-group' },
  { label: 'Schedule', value: 'schedule', icon: 'mdi-calendar' },
  { label: 'Stats', value: 'stats', icon: 'mdi-chart-line' },
  { label: 'Playoffs', value: 'playoffs', icon: 'mdi-trophy' },
  { label: 'Draft Board', value: 'draft', icon: 'mdi-view-dashboard-variant' },
  { label: 'League Setup', value: 'setup', icon: 'mdi-cog', adminOnly: true },
  { label: 'Point Values', value: 'pokemon', icon: 'mdi-chart-bar', adminOnly: true },
]

const leaguePaths: Partial<Record<string, LeagueTab>> = {
  '/': 'home',
  '/team': 'team',
  '/team/manage': 'manage',
  '/activity': 'activity',
  '/matchup': 'matchup',
  '/teams': 'teams',
  '/schedule': 'schedule',
  '/stats': 'stats',
  '/playoffs': 'playoffs',
  '/draft': 'draft',
  '/league/setup': 'setup',
  '/pokemon': 'pokemon',
}

export const leagueWorkflowPaths = new Set(['/league', ...Object.keys(leaguePaths)])

export function isLeagueTab(value: unknown): value is LeagueTab {
  return leagueTabs.some((tab) => tab.value === value)
}

export function leagueTabFromLocation(path: string, queryTab: unknown): LeagueTab {
  const value = Array.isArray(queryTab) ? queryTab[0] : queryTab
  if (isLeagueTab(value)) return value
  return leaguePaths[path] ?? 'home'
}
