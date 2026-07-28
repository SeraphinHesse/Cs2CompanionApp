import { bindValue, useValue } from "cs2/api";
import styles from "./DebugPanel.module.scss";

/**
 * M0 pipeline proof: renders values published by AgoraDebugUISystem.
 *
 * Bindings are addressed as (group, name) and every one is registered in
 * docs/contracts/ui_bindings.md. The third argument is the fallback used before C# has
 * published anything — without it the panel renders `undefined` on the first frame.
 */
const simDate$ = bindValue<string>("agora.debug", "simDate", "");
const simDay$ = bindValue<number>("agora.debug", "simDay", 0);
const enabled$ = bindValue<boolean>("agora.debug", "enabled", false);

export const DebugPanel = () => {
  const simDate = useValue(simDate$);
  const simDay = useValue(simDay$);
  const enabled = useValue(enabled$);

  // Hide entirely when the master toggle is off, rather than showing a disabled panel —
  // "off" should mean the player sees no trace of the mod.
  if (!enabled) {
    return null;
  }

  return (
    <div className={styles.panel}>
      <div className={styles.title}>AGORA</div>
      <div className={styles.row}>
        <span className={styles.label}>Date</span>
        <span className={styles.value}>{simDate || "—"}</span>
      </div>
      <div className={styles.row}>
        <span className={styles.label}>Day of year</span>
        <span className={styles.value}>{simDay > 0 ? simDay : "—"}</span>
      </div>
    </div>
  );
};
