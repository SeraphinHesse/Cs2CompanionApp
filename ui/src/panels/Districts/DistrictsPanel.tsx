import { useMemo } from "react";
import { useValue } from "cs2/api";
import { Scrollable } from "cs2/ui";
import { PanelBoundary } from "./Boundary";
import { CityView } from "./CityView";
import { DistrictDetailPane } from "./DistrictDetail";
import { DistrictList } from "./DistrictList";
import {
  EMPTY_CITY_INDICES,
  EMPTY_STATE_SUMMARY,
  cityCrosstab$,
  cityIndices$,
  districtList$,
  enabled$,
  ready$,
  roster$,
  selectedDistrictId$,
  summary$,
} from "./bindings";
import { indexParties } from "./format";
import paneStyles from "./DetailPane.module.scss";
import styles from "./DistrictsPanel.module.scss";

/**
 * Panel 24 - Districts. Per-district vote splits and the wealth x education crosstab.
 *
 * Reads only bindings frozen in docs/contracts/ui_bindings.md and computes no politics of its own:
 * every share, index and count on screen was published by the engine.
 */

function findDistrict(list: Agora.DistrictBrief[], id: string): Agora.DistrictBrief | null {
  for (let i = 0; i < list.length; i++) {
    if (list[i].id === id) {
      return list[i];
    }
  }
  return null;
}

const DistrictsPanelInner = (): JSX.Element | null => {
  const enabled = useValue(enabled$);
  const ready = useValue(ready$);
  const rawSummary = useValue(summary$);
  const rawRoster = useValue(roster$);
  const rawDistricts = useValue(districtList$);
  const rawCityCells = useValue(cityCrosstab$);
  const rawCityIndices = useValue(cityIndices$);
  const selectedId = useValue(selectedDistrictId$);

  // A binding can hand over a null payload during a partial deploy; the fallback argument only
  // covers the frames before the first publish. Guard rather than let a null reach a field.
  const summary: Agora.StateSummary = rawSummary || EMPTY_STATE_SUMMARY;
  const roster: Agora.PartyBrief[] = rawRoster || [];
  const districts: Agora.DistrictBrief[] = rawDistricts || [];
  const cityCells: Agora.CrosstabCell[] = rawCityCells || [];
  const cityIndices: Agora.CityIndices = rawCityIndices || EMPTY_CITY_INDICES;

  const parties = useMemo(() => indexParties(roster), [roster]);

  const fallbackCount = useMemo(() => {
    let count = 0;
    for (let i = 0; i < districts.length; i++) {
      if (districts[i].hasCityFallbacks) {
        count = count + 1;
      }
    }
    return count;
  }, [districts]);

  // Master toggle off means the player sees no trace of the mod - not a disabled shell.
  if (!enabled) {
    return null;
  }

  const selected = selectedId ? findDistrict(districts, selectedId) : null;

  return (
    <div className={styles.panel}>
      <div className={styles.header}>
        <div className={styles.title}>AGORA / DISTRICTS</div>
        <div className={styles.headerMeta}>
          <span className={styles.headerDate}>{summary.date || "-"}</span>
          <span className={styles.headerSystem}>
            {summary.system === "FirstPastThePost" ? "First past the post" : "Proportional"}
          </span>
        </div>
      </div>

      {!ready ? (
        <div className={styles.skeleton}>
          <div className={styles.skeletonTitle}>Waiting for the first political tick</div>
          <div className={styles.skeletonBody}>
            The engine has not published a political state yet. District splits and crosstabs appear
            on the first monthly tick after the save loads.
          </div>
        </div>
      ) : (
        <div className={styles.body}>
          <DistrictList
            districts={districts}
            parties={parties}
            selectedId={selectedId}
            fallbackCount={fallbackCount}
            onSelect={(id: string) => selectedDistrictId$.update(id)}
          />
          <div className={styles.detailColumn}>
            <Scrollable vertical className={styles.detailScroll}>
              {selectedId === "" ? (
                <CityView
                  cells={cityCells}
                  indices={cityIndices}
                  parties={parties}
                  districtCount={districts.length}
                  fallbackCount={fallbackCount}
                />
              ) : selected ? (
                // Keyed by district id so switching districts remounts the pane and its two map
                // binding subscriptions, rather than re-keying live subscriptions in place.
                <DistrictDetailPane
                  key={selectedId}
                  districtId={selectedId}
                  brief={selected}
                  parties={parties}
                  system={summary.system}
                />
              ) : (
                <div className={paneStyles.pane}>
                  <div className={paneStyles.paneHead}>
                    <div className={paneStyles.paneTitleBlock}>
                      <div className={paneStyles.paneName}>District gone</div>
                      <div className={paneStyles.paneId}>{selectedId}</div>
                    </div>
                  </div>
                  <div className={paneStyles.notice}>
                    This district is no longer in the published list - the player most likely
                    deleted it. Pick another district, or the city-wide view.
                  </div>
                  <div className={styles.backLink} onClick={() => selectedDistrictId$.update("")}>
                    Back to city-wide
                  </div>
                </div>
              )}
            </Scrollable>
          </div>
        </div>
      )}
    </div>
  );
};

export const DistrictsPanel = (): JSX.Element => (
  <PanelBoundary>
    <DistrictsPanelInner />
  </PanelBoundary>
);
