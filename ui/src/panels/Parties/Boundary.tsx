import { Component, ErrorInfo, ReactNode } from "react";
import styles from "./PartiesPanel.module.scss";

interface BoundaryProps {
  children: ReactNode;
}

interface BoundaryState {
  failed: boolean;
  message: string;
}

/**
 * A render failure inside a moduleRegistry-appended component can blank the game's whole UI, and
 * this panel reads six bindings across two publishers. `useMapValue` throws outright if the C#
 * side has not registered the map binding, which is a real state during a partial deploy - so the
 * panel contains its own failures instead of taking the game's interface down with it.
 */
export class PanelBoundary extends Component<BoundaryProps, BoundaryState> {
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
    console.error("[AGORA] Parties panel failed to render", error, info);
  }

  render(): ReactNode {
    if (this.state.failed) {
      return (
        <div className={styles.panel}>
          <div className={styles.header}>
            <div className={styles.title}>AGORA / PARTIES</div>
          </div>
          <div className={styles.boundary}>
            The parties panel stopped rendering. The rest of the interface is unaffected; the error
            is in the game's UI log.
            {this.state.message ? (
              <span className={styles.boundaryDetail}>{this.state.message}</span>
            ) : null}
          </div>
        </div>
      );
    }
    return <>{this.props.children}</>;
  }
}
