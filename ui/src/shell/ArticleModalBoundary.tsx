import { Component, ErrorInfo, ReactNode } from "react";
import { Button } from "cs2/ui";

import { ackAlert } from "./bindings";
import styles from "./ArticleModal.module.scss";

interface BoundaryProps {
  children: ReactNode;
}

interface BoundaryState {
  failed: boolean;
  dismissed: boolean;
}

/**
 * Contains a render failure inside the article modal.
 *
 * Modelled on `FirstRunBoundary`, and needed for the same reason with the same aggravation. A throw
 * inside a `moduleRegistry.append`ed subtree can blank the game's entire interface, and this subtree
 * is one that may be holding the simulation paused: an uncaught throw here would take the HUD down
 * *and* leave the clock stopped. Catching it fixes both at once — React unmounts the children, which
 * runs the pause hook's cleanup, which releases the barrier and hands the player their speed back.
 *
 * The fallback also acks, and that is not tidiness. The barrier is refcounted and released by the
 * unmount above, but the engine still holds the queue: a card that cannot render is a card the player
 * cannot answer, and every later alert would queue behind it. `"*"` clears the lot, which is the only
 * honest thing to send when we cannot say which one broke — the modal is what knew that, and it is
 * the thing that failed.
 */
export class ArticleModalBoundary extends Component<BoundaryProps, BoundaryState> {
  constructor(props: BoundaryProps) {
    super(props);
    this.state = { failed: false, dismissed: false };
    this.dismiss = this.dismiss.bind(this);
  }

  static getDerivedStateFromError(): Partial<BoundaryState> {
    return { failed: true };
  }

  componentDidCatch(error: unknown, info: ErrorInfo): void {
    console.error("[AGORA] the news alert failed to render", error, info);
  }

  private dismiss(): void {
    // Fire and forget: there is nothing left to render a rejection into, and `ackAlert` carries its
    // own deadline and never rejects. The notice already says where to find what was missed.
    void ackAlert("*");
    // Stay in the failed state rather than re-rendering the children. Whatever threw is still there
    // and would throw again on the next frame; `dismissed` gets the notice off the screen without
    // arming a catch-rerender-throw loop. No further alert pops this session, which is the same
    // trade `FirstRunBoundary` makes — the News tab still has every one of them.
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
          A news alert stopped rendering, so Agora has cleared the ones waiting and will not
          interrupt again this session. Nothing was lost — every item is still in the News tab of
          the Agora dashboard.
          <Button variant="flat" className={styles.failureAction} onSelect={this.dismiss}>
            Continue
          </Button>
        </div>
      );
    }
    return <>{this.props.children}</>;
  }
}
