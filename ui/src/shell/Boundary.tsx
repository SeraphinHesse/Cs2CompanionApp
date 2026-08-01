import { Component, ErrorInfo, ReactNode } from "react";
import { Button } from "cs2/ui";

import { closeDashboard } from "./state";
import styles from "./Shell.module.scss";

interface BoundaryProps {
  children: ReactNode;
}

interface BoundaryState {
  failed: boolean;
  message: string;
}

/**
 * Contains a render failure anywhere under the dashboard.
 *
 * This got more load-bearing when the three panels moved behind one mount point: they used to be
 * three independent `moduleRegistry.append` calls, so a throw in one left the other two alone.
 * Now they share this subtree, and a render failure inside a moduleRegistry-appended component can
 * blank the game's entire UI. The Districts panel keeps its own inner boundary — this one is the
 * backstop for Council and News, and for the shell chrome itself.
 *
 * The fallback keeps a working close button. A dashboard that has broken *and* cannot be dismissed
 * is worse than the failure it is reporting.
 */
export class ShellBoundary extends Component<BoundaryProps, BoundaryState> {
  constructor(props: BoundaryProps) {
    super(props);
    this.state = { failed: false, message: "" };
  }

  static getDerivedStateFromError(error: unknown): BoundaryState {
    return {
      failed: true,
      message: error instanceof Error ? error.message : String(error),
    };
  }

  componentDidCatch(error: unknown, info: ErrorInfo): void {
    console.error("[AGORA] dashboard failed to render", error, info);
  }

  render(): ReactNode {
    if (this.state.failed) {
      return (
        <div className={styles.shell}>
          <div className={styles.bar}>
            <span className={styles.barTitle}>AGORA</span>
            <div className={styles.tabs} />
            <Button variant="flat" className={styles.close} onSelect={closeDashboard}>
              &#215;
            </Button>
          </div>
          <div className={styles.failure}>
            The dashboard stopped rendering. The rest of the interface is unaffected; the error is
            in the game&apos;s UI log.
            {this.state.message ? (
              <span className={styles.failureDetail}>{this.state.message}</span>
            ) : null}
          </div>
        </div>
      );
    }
    return <>{this.props.children}</>;
  }
}
