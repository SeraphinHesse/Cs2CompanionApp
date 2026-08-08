import styles from "./Flags.module.scss";

/**
 * The European and American flags, as markup plus CSS and nothing else.
 *
 * See Flags.module.scss for why there is no image asset. Both are decorative: the choice they sit
 * above is labelled and described in words, so a flag that fails to draw costs nothing a player
 * needs to make the decision.
 */

/** Twelve stars, indices 0–11, placed on a ring by the stylesheet. */
const EU_STARS = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11];

/** Thirteen stripes, red first and red last. */
const US_STRIPES = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];

/** Rows of stars in the canton: 5, 4, 5, 4, staggered the way the real one is. */
const US_STAR_ROWS = [5, 4, 5, 4];

export const EuFlag = (): JSX.Element => (
  <div className={styles.euFlag}>
    {EU_STARS.map(function (index) {
      return <div key={index} className={styles["euStar" + index]} />;
    })}
  </div>
);

export const UsFlag = (): JSX.Element => (
  <div className={styles.usFlag}>
    {US_STRIPES.map(function (index) {
      return (
        <div
          key={index}
          className={index % 2 === 0 ? styles.usStripeRed : styles.usStripeWhite}
        />
      );
    })}
    <div className={styles.usCanton}>
      {US_STAR_ROWS.map(function (count, row) {
        return (
          <div key={row} className={styles.usStarRow}>
            {/* A count, not a list — the stars carry no data, only the row width does. */}
            {Array.from({ length: count }, function (_unused, star) {
              return <div key={star} className={styles.usStar} />;
            })}
          </div>
        );
      })}
    </div>
  </div>
);
