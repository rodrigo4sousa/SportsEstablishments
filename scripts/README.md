# Scripts

- `check-architecture-docs.sh` / `.ps1`: checks the architecture docs (links, gallery, diagram aliases). Run by CI.
- `generate-icons.mjs`: makes `frontend/src/app/favicon.ico` (16, 32 and 48 px) and `apple-icon.png` (180 px) from `frontend/src/app/icon.svg`, which is the source of truth. Run it after changing the SVG and commit the results. It uses the Chromium of the E2E tests and adds no dependency to the project:

  ```bash
  cd e2e && npm ci && npx playwright install chromium && cd ..
  node scripts/generate-icons.mjs
  ```

The icon is an original drawing made for this project, under the project's MIT licence.
