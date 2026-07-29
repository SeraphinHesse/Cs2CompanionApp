# ui/ — build notes

The build config here (`package.json`, `tsconfig.json`, `webpack.config.js`, `tools/`, `types/`) came
verbatim from the toolchain's own UI template at:

```
%CSII_INSTALLATIONPATH%\Cities2_Data\Content\Game\.ModdingToolchain\npx-create-csii-ui-mod\template
```

The template ships **locally with the game**, not from the public npm registry — `npx
create-csii-ui-mod` just copies that folder. Since it is on disk, prefer copying from it directly and
diffing against it after a game update. `npm run update` invokes the template's own updater.

`types/*.d.ts` are the `cs2/*` typings. The `cs2/api`, `cs2/ui`, `cs2/bindings`, `cs2/modding`,
`cs2/l10n`, `cs2/input`, `cs2/utils` and `cohtml/cohtml` modules are **webpack externals resolved from
`window` at runtime** — they are not npm dependencies and will never appear in `node_modules`. That is
why an invented `package.json` could not have worked.

## Two things that will bite

**1. webpack writes straight into the deployed mod folder.** From `webpack.config.js`:

```js
const OUTPUT_DIR = `${CSII_USERDATAPATH}\\Mods\\${MOD.id}`;
```

So there is no `dist/`, and no MSBuild copy step is needed — the bundle lands in
`…\Mods\Agora.Mod\` itself. **But `DeployWIP` in the toolchain's `Mod.targets` does `RemoveDir` on
that same folder before copying the C# output.** A C# build therefore deletes the UI bundle.

Order is: build C#, *then* build UI. If you wire the UI build into MSBuild, it must run
**`AfterTargets="DeployWIP"`**, never before it.

**2. webpack reads `process.env.CSII_USERDATAPATH`** and throws if it is missing. Unlike `Mod.props`,
which reads the registry, this needs the variable in the *current process*. A shell opened before the
toolchain was installed will fail here. Open a new one, or set it for the session.

`mod.json` must carry `id`, `author`, `version` and `dependencies` — `webpack.config.js` calls
`MOD.dependencies.join(",")` and crashes on an absent array. `id` must equal `Agora.Mod`'s assembly
name, which is also the deploy folder name, so all three agree.

## Build

```
npm install
npm run build     # production bundle, straight to the Mods folder
npm run dev       # webpack --watch
```

## Node version

`package.json` declares `"node": ">=18"`, so Node 24 is fine. (An earlier note here claimed the
template needed 20.11 — that was inherited from the plan, not from the template itself.)

## Verifying

Launch the game with `--uiDeveloperMode` and open `http://localhost:9444/` for the UI debugger.
`npm run dev` gives hot reload against the running game.
