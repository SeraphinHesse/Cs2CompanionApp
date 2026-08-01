import { useValue } from "cs2/api";
import { Button } from "cs2/ui";

import { enabled$, ready$, simDate$, simMonth$ } from "./bindings";
import { dashboardOpen$, toggleDashboard } from "./state";
import styles from "./Shell.module.scss";

/**
 * The one thing Agora puts on screen unconditionally: the dashboard toggle.
 *
 * Mounted at GameTopLeft from ui/src/index.tsx. It is deliberately the only always-visible control
 * the mod adds — every panel hangs off this button now, so a player who never presses it gets their
 * whole viewport back.
 *
 * It also absorbed the M0 DebugPanel. That panel existed to answer "is the C# → JS bridge alive?"
 * from values that depend on no engine state at all, and that question is worth answering from the
 * one element that is always mounted rather than from a second card in the same corner. The sim
 * date on the face and the month in the tooltip are the same two readings the panel showed.
 *
 * The dot is the other half of the diagnosis. Dim means the engine has not published a political
 * state yet (`agora.state.ready` false) — normal for the first political month after a save loads,
 * and otherwise the first sign a publisher failed. Without it, "the dashboard is empty" and "the
 * dashboard never received any data" look identical from the outside.
 */
export const AgoraButton = () => {
  const enabled = useValue(enabled$);
  const ready = useValue(ready$);
  const simDate = useValue(simDate$);
  const simMonth = useValue(simMonth$);
  const open = useValue(dashboardOpen$);

  // Every hook is above this line — the master toggle must not change the hook order.
  // Off means no trace of the mod, so render nothing rather than a disabled control.
  if (!enabled) {
    return null;
  }

  const tooltip =
    (open ? "Close the Agora dashboard" : "Open the Agora dashboard") +
    (ready ? "" : " — waiting for the first political tick") +
    (simMonth > 0 ? " · month " + simMonth : "");

  return (
    <Button
      variant="flat"
      className={open ? styles.toggleOpen : styles.toggle}
      selected={open}
      onSelect={toggleDashboard}
      tooltipLabel={tooltip}
    >
      {/* A span, not a div: this renders inside a <button>, and Gameface is not the place to
          find out how a block element nested in a button is handled. The dot's box comes from
          the stylesheet's `display: flex`. */}
      <span className={ready ? styles.toggleDot : styles.toggleDotWaiting} />
      <span className={styles.toggleLabel}>AGORA</span>
      {/* Empty until the clock is readable, which is every frame outside a loaded game. */}
      {simDate ? <span className={styles.toggleDate}>{simDate}</span> : null}
    </Button>
  );
};
