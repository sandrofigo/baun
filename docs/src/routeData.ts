import { defineRouteMiddleware } from '@astrojs/starlight/route-data';

// Starlight titles pages "<page> | <site>", which makes the home page "baun | baun".
// Use only the site title there.
export const onRequest = defineRouteMiddleware((context) => {
	const route = context.locals.starlightRoute;
	if (route.id !== '') return;

	const title = route.head.find((entry) => entry.tag === 'title');
	if (title) title.content = route.siteTitle;
});
