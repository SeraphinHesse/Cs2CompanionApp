import { Component, ErrorInfo, ReactNode } from "react";
import { Button } from "cs2/ui";

import { requestSetting } from "./bindings";
import styles from "./FirstRunDialog.module.scss";

interface BoundaryProps {
  children: ReactNode;
}

interface BoundaryState {
  failed: boolean;
  dismissed: boolean;
}

/**
 * Contains a render failure inside the first-run prompt.
 *
 * Modelled on `ShellBoundary`, and needed for the same reason with one aggravation. A throw inside a
 * `moduleRegistry.append`ed subtree can blank the game's entire interface, and this subtree is the
 * one holding the simulation paused: an uncaught throw here would take the HUD down *and* leave the
 * clock stopped. Catching it fixes both at once — React unmounts the children, which runs the pause
 * hook's cleanup, which releases the barrier and hands the player their speed back.
 *
 * The fallback is not blank. `isFirstRun` is still true, so the save has no chosen region and the
 * player has no way to say so; the button below sends the same dismissal the dialog would have sent
 * and lets the save carry on with the default. A prompt that has broken *and* cannot be answered is
 * worse than the failure it is reporting.
 */
export class FirstRunBoundary extends Component<BoundaryProps, BoundaryState> {
  constructor(props: BoundaryProps) {
    super(props);
    this.state = { failed: false, dismissed: false };
    this.dismiss = this.dismiss.bind(this);
  }

  static getDerivedStateFromError(): Partial<BoundaryState> {
    return { failed: true };
  }

  componentDidCatch(error: unknown, info: ErrorInfo): void {
    console.error("[AGORA] the first-run prompt failed to render", error, info);
  }

  private dismiss(): void {
    // Fire and forget: there is nothing left to render a rejection into. The notice already says
    // what happens if this does not land, and the settings surface remains the way back.
    void requestSetting("dismissFirstRun", "");
    // Stay in the failed state rather than re-rendering the children. Whatever threw is still there
    // and would throw again on the next frame; `dismissed` gets the notice off the screen without
    // arming a catch-rerender-throw loop.
    this.setState({ dismissed: true });
  }

  render(): ReactNode {
    if (this.state.failed) {
      if (this.state.dismissed) {
        return null;
      }
      // Deliberately NOT through `Portal`. `Portal` is one of the things that can throw here — it
      // reads a container off a context this subtree does not own — and a fallback that rethrows has
      // no boundary above it but the game's. So the notice renders inline at the hook point: a card
      // in a corner instead of a centred overlay, which is the right trade when the alternative is a
      // blank interface.
      return (
        <div className={styles.failure}>
          The region prompt stopped rendering, so this city starts in Europe — proportional seats
          and coalition governments. You can change that from Settings in the Agora dashboard, right
          up until the first election.
          <Button variant="flat" className={styles.failureAction} onSelect={this.dismiss}>
            Continue
          </Button>
        </div>
      );
    }
    return <>{this.props.children}</>;
  }
}
