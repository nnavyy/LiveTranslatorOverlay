# Repository working preferences

- After completing requested work and running the appropriate verification, commit and push the scoped changes to Git. The user has explicitly requested this as the default; do not leave completed work only on the local machine.
- For user-facing application fixes or features, publish a versioned release through the existing `v*` tag workflow so the in-app updater can discover the change. Update the application version consistently and provide release notes describing verified improvements and bug fixes. A push to `main` alone does not publish an application update.
- Documentation-only changes, including review reports, should be committed and pushed without creating an application release.
- Do not publish claims that a reported bug is fixed until its fix has been verified. Keep unrelated work and secrets out of commits; never force-push as part of the default workflow.

- Future application implementation is private by explicit user request. Push app source only to the private remote (nnavyy/LiveTranslatorOverlay-private). Public origin is for downloads/docs and historical source only; use its separate public-downloads worktree for documentation updates. Do not push new app source to public origin.
