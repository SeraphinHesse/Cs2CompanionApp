import { useMemo } from "react";
import { useMapValue } from "cs2/api";

import { EMPTY_STORY_ARTICLE } from "../../shell/bindings";
import { article$ } from "./bindings";
import { splitParagraphs } from "./format";
import styles from "./StoriesPanel.module.scss";

/**
 * One story's prose, fetched for exactly this story id.
 *
 * A component of its own for the reason `ArticleModal.tsx` documents for the news article map:
 * `useMapValue` may not be called conditionally, so the condition lives in the **mount**, not in the
 * call. The parent renders this only for a story that has an id, keyed by that id, so a changing
 * story remounts the subscription rather than re-keying a live one. Do not hoist the hook.
 *
 * **Two voices, and both render when both exist.** The canned pool answers every poll and always has
 * an answer; the model answers rarely. The pool half is what is always shown and the model's appears
 * BESIDE it, never instead of it — showing only the newest would rewrite, within a minute, text the
 * player had already read. That is an owner decision from wave 5 (`docs/plans/0004-wave-5-handoff.md`)
 * and contract §4.7 states it as a rule, not as a preference.
 *
 * There is no "still fetching" state to render and nothing here may pretend otherwise: a map binding
 * resolves inside its own subscribe trigger, so what comes back is C#'s final answer for this id. An
 * id the projection does not know answers the empty payload rather than throwing (contract §6).
 *
 * Every field is FLAVOR. Render it; parse none of it.
 */
export const StoryBody = (props: { storyId: string }): JSX.Element => {
  const fetched = useMapValue(article$, props.storyId) as Agora.StoryArticle | undefined;
  const article: Agora.StoryArticle = fetched || EMPTY_STORY_ARTICLE;

  const poolParagraphs = useMemo(
    function () {
      return splitParagraphs(article.poolArticle);
    },
    [article.poolArticle],
  );

  const cliParagraphs = useMemo(
    function () {
      return splitParagraphs(article.cliArticle);
    },
    [article.cliArticle],
  );

  // The model has written about this story only when there is something of its to show. Absent is the
  // ordinary case and not an error, so nothing is said about it.
  const hasCli = cliParagraphs.length > 0 || !!article.cliHeadline;

  if (poolParagraphs.length === 0 && !hasCli) {
    return (
      <div className={styles.bodyNote}>
        No write-up has reached this story yet. The events below are the whole of what is known.
      </div>
    );
  }

  return (
    <div className={styles.voices}>
      {poolParagraphs.length > 0 ? (
        <div className={styles.voice}>
          <div className={styles.voiceLabel}>The word around town</div>
          {article.poolHeadline ? (
            <div className={styles.voiceHeadline}>{article.poolHeadline}</div>
          ) : null}
          {poolParagraphs.map(function (paragraph, index) {
            return (
              <div key={index} className={styles.para}>
                {paragraph}
              </div>
            );
          })}
        </div>
      ) : null}

      {hasCli ? (
        <div className={styles.voice}>
          <div className={styles.voiceLabel}>Written up at greater length</div>
          {article.cliHeadline ? (
            <div className={styles.voiceHeadline}>{article.cliHeadline}</div>
          ) : null}
          {cliParagraphs.map(function (paragraph, index) {
            return (
              <div key={index} className={styles.para}>
                {paragraph}
              </div>
            );
          })}
        </div>
      ) : null}
    </div>
  );
};
