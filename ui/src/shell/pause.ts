import { useEffect } from "react";
import { time } from "cs2/bindings";

/**
 * Hold the simulation paused for as long as a component wants it held.
 *
 * `time.simulationPausedBarrier$` is an `EventBinding<boolean>` and the game's own refcounted pause.
 * `TimeUISystem` forces the speed to zero every frame while at least one observer is subscribed, and
 * restores the speed the player had when the count reaches zero. Subscribing is the whole of the
 * pause; disposing is the whole of the un-pause.
 *
 * This is deliberately not a write to `SimulationSystem.selectedSpeed`, which is what an earlier
 * plan proposed. That setter is a no-op while the game is loading and the game re-applies a speed of
 * its own once loading completes, so the one write that matters — the one issued as a save comes up,
 * which is exactly when the first-run dialog opens — is silently discarded. Capturing and restoring
 * the prior speed by hand has the same problem from the other end: it makes "the dashboard closed
 * without restoring" a bug that has to be handled, on unmount, on save load, on quit to menu and in
 * a boundary catch. Subscribing to the barrier makes it a case that cannot arise: React runs the
 * cleanup in all four, and the restore is the game's code, not ours.
 *
 * `TimeUISystem` exists only inside a loaded game, so the subscribe is guarded. Outside one there is
 * no simulation to pause and failing to take the barrier is the correct outcome, not an error.
 */
export function useSimulationHeldPaused(active: boolean): void {
  useEffect(
    function () {
      if (!active) {
        return undefined;
      }

      let subscription: { dispose(): void } | null = null;
      try {
        // The listener is required by the binding and is of no interest to us: the pause is the
        // subscription itself, not anything the barrier reports back.
        subscription = time.simulationPausedBarrier$.subscribe(function () {
          /* the subscription is the effect */
        });
      } catch (error) {
        console.warn("[AGORA] could not take the simulation pause barrier", error);
        return undefined;
      }

      return function () {
        try {
          if (subscription !== null) {
            subscription.dispose();
          }
        } catch (error) {
          console.warn("[AGORA] could not release the simulation pause barrier", error);
        }
      };
    },
    [active],
  );
}
