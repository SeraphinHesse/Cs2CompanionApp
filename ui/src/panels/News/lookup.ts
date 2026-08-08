import { useMemo } from "react";
import { useValue } from "cs2/api";

import { districts$, roster$ } from "./bindings";

/**
 * Id -> label resolution for the News panel.
 *
 * News items, events and mandates all carry ids only. Party name and colour come from
 * `agora.parties.roster` and district names from `agora.districts.list`, which is the whole point
 * of those bindings: resolve in one place so a party cannot end up two different colours in two
 * panels. Nothing here iterates a map in a way that reaches engine state — these are point
 * lookups for rendering.
 */

/** Used when a party id is empty or has aged out of the roster. */
const NEUTRAL_COLOR = "#8a8f98";

/**
 * Shown wherever a party exists but has no usable name yet — either it has aged out of the roster
 * or the flavor layer has not authored one. A raw id is never rendered to the player.
 */
const UNNAMED_PARTY = "Unnamed party";

export interface Lookups {
  party(id: string): Agora.PartyBrief | undefined;
  /** Always a usable "#RRGGBB" — falls back to neutral rather than rendering a broken colour. */
  partyColor(id: string): string;
  /** "" for an absent party id, so callers can skip the chip entirely. */
  partyLabel(id: string): string;
  /** "Citywide" for an absent district id; the raw id if the district is not in the list. */
  districtLabel(id: string): string;
}

export function useLookups(): Lookups {
  const roster = useValue(roster$);
  const districts = useValue(districts$);

  return useMemo<Lookups>(() => {
    const partiesById: { [id: string]: Agora.PartyBrief } = {};
    for (let i = 0; i < roster.length; i++) {
      partiesById[roster[i].id] = roster[i];
    }

    const districtsById: { [id: string]: Agora.DistrictBrief } = {};
    for (let i = 0; i < districts.length; i++) {
      districtsById[districts[i].id] = districts[i];
    }

    return {
      party(id: string) {
        return id ? partiesById[id] : undefined;
      },
      partyColor(id: string) {
        const party = id ? partiesById[id] : undefined;
        return party && party.colorHex ? party.colorHex : NEUTRAL_COLOR;
      },
      partyLabel(id: string) {
        if (!id) {
          return "";
        }
        const party = partiesById[id];
        if (!party) {
          // A dissolved party can still be named by an old headline. Show the placeholder rather
          // than nothing, so the item does not silently lose its subject.
          return UNNAMED_PARTY;
        }
        return party.shortName || party.name || UNNAMED_PARTY;
      },
      districtLabel(id: string) {
        if (!id) {
          return "Citywide";
        }
        const district = districtsById[id];
        return district && district.name ? district.name : id;
      },
    };
  }, [roster, districts]);
}
