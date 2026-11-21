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
			customCss: ['./src/styles/custom.css'],
			routeMiddleware: './src/routeData.ts',
			description: 'A build pipeline system for .NET that lets you write any kind of workflow in plain C#.',
			social: [{ icon: 'github', label: 'GitHub', href: 'https://github.com/sandrofigo/baun' }],
			editLink: {
				baseUrl: 'https://github.com/sandrofigo/baun/edit/main/docs/',
			},
			sidebar: [
				{ label: 'Getting started', slug: 'getting-started' },
				{
					label: 'Guides',
					items: [
						'guides/concept',
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
					label: 'Reference',
					items: ['reference/cli'],
				},
				{
					label: 'Contributing',
					items: ['contributing/development'],
				},
			],
		}),
	],
});
