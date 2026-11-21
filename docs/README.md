# baun docs

The baun documentation site, built with [Starlight](https://starlight.astro.build).

```shell
npm install
npm run dev      # preview at http://localhost:4321/baun/
npm run build    # build to dist/
```

Pages are Markdown files in `src/content/docs/`. Add new pages to the sidebar in `astro.config.mjs`. Link between pages with relative links, such as `../parameters/`, so they work under the `/baun/` base path.

The [Docs](../.github/workflows/docs.yml) workflow deploys the site to GitHub Pages when `docs/` changes on `main`.
