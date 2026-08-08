/**
 * The two region themes, and what choosing one does to the save.
 *
 * The copy lives here rather than in either surface because both the first-run dialog and the
 * settings panel put the same choice in front of the player, and a player who reads one sentence on
 * the prompt and a different one in settings has been told they are two different settings.
 *
 * English only, no localisation layer (non-negotiable #10). These are plain strings, not l10n keys.
 *
 * The consequence lines are the ones ratified in fixplan.md §W3. Each is one line: the theme drives
 * the electoral system, the naming vocabulary, the term length and which timeline catalogs apply,
 * and no player is going to read a paragraph about that before the game has started.
 */

export interface RegionChoice {
  theme: Agora.RegionThemeName;
  /** What the player calls it. Never the wire value — "Eu" is engine vocabulary. */
  label: string;
  consequence: string;
}

export const REGION_CHOICES: RegionChoice[] = [
  {
    theme: "Eu",
    label: "Europe",
    consequence: "Proportional list seats, 4–7 parties, coalition governments, 3-year terms.",
  },
  {
    theme: "Na",
    label: "United States",
    consequence:
      "First-past-the-post district races, a directly elected mayor, two dominant parties " +
      "with internal factions, 4-year terms.",
  },
];

/**
 * Wire value to display name, for the places that have a theme and need a word for it.
 *
 * Falls through to the raw value the way the News panel's `ORIGIN_LABEL` does: a third theme added
 * on the C# side should appear as itself rather than silently render as Europe.
 */
const REGION_LABEL: { [theme: string]: string } = {
  Eu: "Europe",
  Na: "United States",
};

export function regionLabel(theme: string): string {
  return REGION_LABEL[theme] || theme;
}
