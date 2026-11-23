// @ts-check
import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';

// https://astro.build/config
export default defineConfig({
	site: 'https://baun.dev',
	integrations: [
		starlight({
			title: 'baun',
			logo: {
				src: './src/assets/baun_logotype.png',
				replacesTitle: true,
			},
			favicon: '/baun_favicon.png',
			head: [
				{ tag: 'meta', attrs: { property: 'og:image', content: 'https://baun.dev/og.png' } },
				{ tag: 'meta', attrs: { name: 'twitter:image', content: 'https://baun.dev/og.png' } },
			],
			customCss: ['./src/styles/custom.css'],
			// VS Code's themes color C# types, methods and keywords apart, which Starlight's default themes don't.
			// The frames and backgrounds of code blocks keep the colors of the site.
			expressiveCode: {
				themes: ['dark-plus', 'light-plus'],
				useStarlightUiThemeColors: true,
			},
			routeMiddleware: './src/routeData.ts',
			components: {
				SocialIcons: './src/components/SocialIcons.astro',
			},
			description: 'A build pipeline system that lets you write any kind of workflow in plain C#.',
			social: [{ icon: 'github', label: 'baun on GitHub', href: 'https://github.com/sandrofigo/baun' }],
			editLink: {
				baseUrl: 'https://github.com/sandrofigo/baun/edit/main/docs/',
			},
			sidebar: [
				{ label: 'Getting Started', slug: 'getting-started' },
				{
					label: 'Guides',
					items: [
						'guides/concept',
						'guides/control-flow',
						'guides/paths',
						'guides/hooks',
						'guides/parameters',
						'guides/logging',
						'guides/tools',
						'guides/solutions-and-projects',
						'guides/ci',
						'guides/project-or-single-file',
					],
				},
				{
					label: 'Tools',
					items: ['tools/dotnet', 'tools/git'],
				},
				{
					label: 'Reference',
					items: ['reference/cli', 'reference/diagnostics'],
				},
				{
					label: 'Contributing',
					items: ['contributing/development'],
				},
			],
		}),
	],
});
