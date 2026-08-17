import { Component, ErrorInfo, ReactNode } from "react";
import { Button } from "cs2/ui";

import { ackStoryAlert } from "./bindings";
import styles from "./StoryModal.module.scss";

interface BoundaryProps {
  children: ReactNode;
}

interface BoundaryState {
  failed: boolean;
  dismissed: boolean;
}

/**
 * Contains a render failure inside the story card.
 *
 * Modelled on `ArticleModalBoundary`, and needed for the same reason with the same aggravation. A
 * throw inside a `moduleRegistry.append`ed subtree can blank the game's entire interface, and this
 * subtree is one that may be holding the simulation paused: an uncaught throw here would take the HUD
 * down *and* leave the clock stopped. Catching it fixes both at once — React unmounts the children,
 * which runs the pause hook's cleanup, which releases the barrier and hands the player their speed
 * back. A separate boundary from the news one because they are separate mounts: a story card that
 * cannot render must not take the news card down with it, or vice versa.
 *
 * The fallback also acks, and that is not tidiness. The barrier is refcounted and released by the
 * unmount above, but the engine still holds the queue: a card that cannot render is a card the player
 * cannot close, and every later story would queue behind it. `"*"` clears the lot, which is the only
 * honest thing to send when we cannot say which one broke — the card is what knew that, and it is the
 * thing that failed.
 *
 * Dismissing here loses nothing, for the reason the card itself is a notification: an ack answers no
 * part of the story. Every story is still live, still unanswered, and still tackled from the Stories
 * tab, which is exactly what the notice below tells the player.
 */
export class StoryModalBoundary extends Component<BoundaryProps, BoundaryState> {
  constructor(props: BoundaryProps) {
    super(props);
    this.state = { failed: false, dismissed: false };
    this.dismiss = this.dismiss.bind(this);
  }

  static getDerivedStateFromError(): Partial<BoundaryState> {
    return { failed: true };
  }

  componentDidCatch(error: unknown, info: ErrorInfo): void {
    console.error("[AGORA] the story card failed to render", error, info);
  }

  private dismiss(): void {
    // Fire and forget: there is nothing left to render a rejection into, and `ackStoryAlert` carries
    // its own deadline and never rejects. The notice already says where to find what was missed.
    void ackStoryAlert("*");
    // Stay in the failed state rather than re-rendering the children. Whatever threw is still there
    // and would throw again on the next frame; `dismissed` gets the notice off the screen without
    // arming a catch-rerender-throw loop. No further story card pops this session, which is the same
    // trade `ArticleModalBoundary` makes — the Stories tab still has every one of them, and unlike a
    // news alert a story is a decision that is still open and still answerable there.
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
          A story card stopped rendering, so Agora has cleared the ones waiting and will not
          interrupt again this session. Nothing was decided and nothing was lost — every story is
          still open in the Stories tab of the Agora dashboard.
          <Button variant="flat" className={styles.failureAction} onSelect={this.dismiss}>
            Continue
          </Button>
        </div>
      );
    }
    return <>{this.props.children}</>;
  }
}
