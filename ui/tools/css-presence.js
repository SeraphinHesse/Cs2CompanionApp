const fs = require("fs");
const path = require("path");

exports.CSSPresencePlugin = class CSSPresencePlugin {
  apply(compiler) {
    // Required here rather than at the top of the file. This module has a second life as the
    // standalone design token guard below, which `npm run check` runs without building the bundle —
    // and a top-level `require("webpack")` would make that path need the full webpack install after
    // all, which is exactly the dependency it advertises not having.
    const { RawSource } = require("webpack").sources;

    compiler.hooks.compilation.tap("CSSPresencePlugin", (compilation) => {
      compilation.hooks.processAssets.tap(
        {
          name: "CSSPresencePlugin",
          stage: compilation.PROCESS_ASSETS_STAGE_ADDITIONS,
        },
        () => {
          const cssFiles = Object.keys(compilation.assets).filter((asset) =>
            asset.endsWith(".css")
          );
          const hasCSS = cssFiles.length > 0;

          // Inject the `hasCSS` export into the main module source
          for (const chunk of compilation.chunks) {
            for (const file of chunk.files) {
              if (file.endsWith(".mjs")) {
                const asset = compilation.getAsset(file);
                const source = asset.source.source();
                const updatedSource = source.replace(
                  "export {",
                  `const hasCSS = ${hasCSS}; export { hasCSS, `
                );

                // Generate a new source map for the modified source
                const newSourceAndMap = {
                  sources: [file],
                  mappings: "",
                  file,
                  sourceRoot: "",
                  sourcesContent: [updatedSource],
                };

                compilation.updateAsset(
                  file,
                  new RawSource(updatedSource, newSourceAndMap)
                );
              }
            }
          }
        }
      );
    });
  }
};

// ---- design token guard (fixplan W1) ------------------------------------------------------------
//
// Four panels used to declare four different `$surface` values and none of them agreed; the lowest
// was 0.62 over a moving city. `src/shell/_tokens.scss` is now the single source, and this guard is
// what keeps it single: a panel that reintroduces a local surface, text, line or status colour
// fails the build rather than drifting quietly until someone plays the game and notices.
//
// Gameface has no `backdrop-filter`, so opacity is the only lever there is. A stray local surface
// is not a cosmetic slip — it is the whole readability problem coming back.

/** The only file allowed to declare these. Relative to `src/`, POSIX separators. */
const TOKEN_SOURCE = "shell/_tokens.scss";

/** Variables that are design tokens rather than per-panel geometry. */
const RESERVED = [
  "surface",
  "surface-raised",
  "surface-inset",
  "surface-hover",
  "surface-track",
  "text",
  "text-dim",
  "text-faint",
  "line",
  "line-soft",
  "line-strong",
  "accent",
  "good",
  "warn",
  "bad",
  "fallback",
  "fallback-line",
  "fallback-wash",
  "fallback-dim",
];

/** `$name:` at the start of a line — a declaration, not a use. */
const DECLARATION = /^[ \t]*\$([a-zA-Z0-9_-]+)[ \t]*:/;

function collectScss(dir, out) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      collectScss(full, out);
    } else if (entry.name.endsWith(".scss")) {
      out.push(full);
    }
  }
  return out;
}

/**
 * Scan every stylesheet under `srcDir` for a locally declared design token.
 *
 * Returns a list of human-readable violations; empty means clean. Files are visited in sorted
 * order so the message is the same on every machine.
 */
exports.findLocalTokenDeclarations = function findLocalTokenDeclarations(srcDir) {
  const reserved = new Set(RESERVED);
  const violations = [];
  const files = collectScss(srcDir, []).sort();

  for (const file of files) {
    const relative = path.relative(srcDir, file).split(path.sep).join("/");
    if (relative === TOKEN_SOURCE) {
      continue;
    }
    const lines = fs.readFileSync(file, "utf8").split("\n");
    for (let i = 0; i < lines.length; i++) {
      const match = DECLARATION.exec(lines[i]);
      if (match && reserved.has(match[1])) {
        violations.push(
          `src/${relative}:${i + 1} declares $${match[1]} locally. ` +
            `Design tokens live in src/${TOKEN_SOURCE} — ` +
            `add \`@use "…/shell/tokens" as *;\` and delete the local declaration.`
        );
      }
    }
  }

  return violations;
};

/** Fails the webpack build if any panel declares a design token of its own. */
exports.DesignTokenPlugin = class DesignTokenPlugin {
  constructor(srcDir) {
    this.srcDir = srcDir;
  }

  apply(compiler) {
    compiler.hooks.thisCompilation.tap("DesignTokenPlugin", (compilation) => {
      const violations = exports.findLocalTokenDeclarations(this.srcDir);
      for (const violation of violations) {
        compilation.errors.push(new Error("design token guard: " + violation));
      }
    });
  }
};

// Standalone entry point, so `npm run check` catches this without a full webpack run — and so the
// check still runs if the bundle build is skipped.
if (require.main === module) {
  const srcDir = path.join(__dirname, "..", "src");
  const violations = exports.findLocalTokenDeclarations(srcDir);
  if (violations.length > 0) {
    console.error("design token guard failed:\n");
    for (const violation of violations) {
      console.error("  " + violation);
    }
    console.error("");
    process.exit(1);
  }
  console.log("design token guard: clean");
}
