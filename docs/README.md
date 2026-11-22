# baun docs

The baun documentation site at [baun.dev](https://baun.dev), built with [Starlight](https://starlight.astro.build).

```shell
npm install
npm run dev      # preview at http://localhost:4321/
npm run build    # build to dist/
```

Pages are Markdown files in `src/content/docs/`. Add new pages to the sidebar in `astro.config.mjs`. Link between pages with relative links, such as `../parameters/`.

The site is served as static assets by Cloudflare Workers, configured in `wrangler.jsonc`. The [Docs](../.github/workflows/docs.yml) workflow builds it on pull requests. The [Release](../.github/workflows/release.yml) workflow calls it after each stable release to deploy the site with Wrangler, so docs merged into `main` stay unpublished until the next release. To deploy a docs fix between releases, see [Releasing](../.github/RELEASING.md#deploying-docs-fixes-between-releases).
