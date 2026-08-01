import { useMemo } from "react";
import { useMapValue } from "cs2/api";
import { Button, Scrollable } from "cs2/ui";

import styles from "./NewsPanel.module.scss";
import { article$, EMPTY_NEWS_ARTICLE } from "./bindings";
import { Lookups } from "./lookup";
import { formatSimDate, splitParagraphs } from "./format";

/**
 * The reading view for one feed item.
 *
 * `agora.news.article` is a MAP binding: the body is fetched for exactly the id being read and
 * never in bulk, which is why prose does not ride in the feed payload at all.
 *
 * This component is mounted only when a feed item with `hasArticle === true` is opened. That
 * matters beyond tidiness: `useMapValue` throws if the binding is not registered on the C# side,
 * and a feed item can only exist after AgoraNewsUISystem has published — so the map is never
 * subscribed before its publisher is alive.
 *
 * Every field here is FLAVOR. Render it; parse none of it.
 */

interface ArticleReaderProps {
  headline: Agora.NewsHeadline;
  lookups: Lookups;
  onClose: () => void;
}

export const ArticleReader = ({ headline, lookups, onClose }: ArticleReaderProps) => {
  // The declared overload returns V, but a map value can genuinely be absent for a frame after
  // subscribing, so the empty value from the contract is used as the floor.
  const fetched = useMapValue(article$, headline.id) as Agora.NewsArticle | undefined;
  const article: Agora.NewsArticle = fetched || EMPTY_NEWS_ARTICLE;

  const paragraphs = useMemo(() => splitParagraphs(article.body), [article.body]);

  // Fall back to the feed row's own strings while the body is still in flight, so opening an
  // item never shows a blank sheet.
  const title = article.headline || headline.headline || "Untitled report";
  const outlet = article.outletName || headline.outletName;
  const date = article.date || headline.date;
  const partyLabel = lookups.partyLabel(article.partyId || headline.partyId);
  const districtId = article.districtId || headline.districtId;

  return (
    <div className={styles.article}>
      <div className={styles.articleBar}>
        <Button variant="flat" className={styles.smallButton} onSelect={onClose}>
          Back to the feed
        </Button>
      </div>

      <Scrollable vertical={true} trackVisibility="scrollable" className={styles.scroll}>
        <div className={styles.articleInner}>
          <div className={styles.metaRow}>
            <span className={styles.metaDate}>{formatSimDate(date)}</span>
            {outlet ? <span className={styles.metaOutlet}>{outlet}</span> : null}
            {article.tone ? <span className={styles.chip}>{article.tone}</span> : null}
            {partyLabel ? <span className={styles.chip}>{partyLabel}</span> : null}
            {districtId ? (
              <span className={styles.chip}>{lookups.districtLabel(districtId)}</span>
            ) : null}
          </div>

          <div className={styles.articleHeadline}>{title}</div>
          {article.byline ? <div className={styles.articleByline}>{article.byline}</div> : null}

          {paragraphs.length > 0 ? (
            paragraphs.map((paragraph, index) => (
              <div key={index} className={styles.articlePara}>
                {paragraph}
              </div>
            ))
          ) : (
            <div className={styles.articlePara}>
              {headline.summary || "The full text of this piece is not available."}
            </div>
          )}

          {article.tags.length > 0 ? (
            <div className={styles.tagRow}>
              {article.tags.map((tag) => (
                <span key={tag} className={styles.chip}>
                  {tag}
                </span>
              ))}
            </div>
          ) : null}
        </div>
      </Scrollable>
    </div>
  );
};
