# Headless workflow smoke instructions

This project supplements the repository root instructions. It is a separate
process so Avalonia, static database paths and transport substitutions cannot
affect the pure desktop unit tests.

- Seed a unique temporary data root and legacy-migration marker before creating
  the application or MainWindow. Never use a personal catalog or credentials.
- Seed valid synthetic output settings before the application too, so Settings
  normalization cannot discover the real default endpoint. Empty-output checks
  modify in-memory settings only. No production default migration is disabled.
- Never show MainWindow, invoke its startup handlers, start playback or
  enumerate output devices. An isolated headless control host may perform
  bounded layout.
- Use fresh cached metadata and synthetic HTTP. Block external profile/image
  transport before application creation and assert that no such requests occur.
- Use valid synthetic inputs. Exercise production adapters and their existing
  history/ownership; do not introduce a second navigation model for tests.
- Every asynchronous gate must be deterministic. Completion timeouts are failure
  diagnostics, not performance thresholds. Pump the dispatcher on its owning
  thread and release gates/hosts/transport during teardown.
- Keep diagnostics fixed and credential-free. Do not print headers, URLs, real
  paths, catalog metadata or environment values.
- The initial runtime contract is Windows-only. Add evidence and CI execution
  before claiming other-platform support. Pure unit tests remain cross-platform.
- Native output-profile checks may simulate Mac choice policy and inject PCM
  device lists into OutputProfileDialog. Never invoke real device enumeration or
  playback. This proves shared control layout/Save behavior only, not native
  macOS windowing, HAL capabilities or audible output.
- Run the project in Debug and Release through `scripts/verify-all.ps1` after
  changes. Windows CI must continue invoking it in both configurations.
- Match the desktop Avalonia version for Headless dependencies. Document all
  fixture types/members in English and maintain root documentation/checklists.

- Search fixtures must distinguish unavailable sources from successful empty
  results, keep partial rows during retry, reject stale metadata registration
  and retain source-aware selection/history through the production Back adapter.
  Keep synthetic request observations in memory and never print them.

- Remote album checks must hold metadata separately from overview/track reads,
  reject stale admission before track requests and keep captured fallback/source
  identity. Exercise both real open overloads and the existing restore adapter;
  release every captured synthetic gate in teardown.

- Local album fixtures must hold dispatcher publication rather than rely on
  worker timing. Preserve newer rows, header, opacity, Back state and loading
  cleanup. A populated journey must use actual search/open/Back adapters, source
  collisions and existing history; state explicitly when pointer dispatch is not
  exercised.

- Disk cache tests must use the process-owned temporary data root and a unique
  synthetic source. Check actual persisted bytes and request counts; distinguish
  failed loads from successful empty responses and reapply current favorites.
  Never write a personal cache or forward synthetic requests to real HTTP.

- Observe actual async-void Retry completion through its synchronization-context
  lifetime, including finally and abandoned requests. Do not infer completion
  from a cleared owner field or fixed sleeps. Continuations must return to the
  owning UI dispatcher; release all captured transport gates during teardown.

- Fresh-profile setup checks run before catalog seeding. Detach only the
  compiled hint, setup-only sidebar or output-profile panel into a bounded host;
  verify wrapped localized captions, focus admission and explicit automation
  names. Activate the routed Click event through the existing Settings host and
  deactivate every opened view. Never start a scan, open a picker, connect a
  real server or enumerate audio devices. Distinguish these checks from native
  Tab/pointer and audible playback proof.
- Headless sidebar key checks temporarily retain only the setup item inside the
  actual compiled ListBox. This avoids activating unrelated radio/provider
  navigation. Restore all items/parent placement and deactivate Settings. Label
  this as headless key dispatch, not native OS or full-sidebar traversal proof.
- Idle transport checks may call StopPlaybackCore only with no attached player;
  assert null detachment and never invoke factory, startup, resume or playback.
- Directory-add checks replace Settings DirectoryScanner with deterministic
  synthetic delegates before admission. Use only temporary empty folders; never
  invoke the real scanner or picker. Check publication before scan, progress,
  duplicate rejection, active-row identity, cancellation and failure recovery.
- Scaling checks use Headless SetRenderScaling at 1/2 with bounded controls.
  ORYNIVO_SMOKE_RENDER_DIR optionally exports synthetic frames outside ordinary
  CI. Keep them uncommitted. Contrast assertions apply only to opaque theme
  colors, not physical monitors or accessibility certification.
- View preview checks use synthetic source/profile identity and deterministic
  held HTTP. A new unshown window may simulate restart in the same temporary
  data root. Verify cached content before network completion, changed/unchanged
  publication, immediate presentation changes, failed/cold local fallback and
  source-aware navigation. Inspect only synthetic preview bytes for secret
  exclusion; never inspect personal caches. Drain shared snapshot tasks and
  optional preview writes before transport teardown. These ordering checks do
  not establish a native first-frame or millisecond latency budget.
