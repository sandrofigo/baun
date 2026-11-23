// Generates the animated diagrams of the Control Flow guide in src/assets/control-flow.
// Run it with `node scripts/control-flow-diagrams.mjs` from the docs folder after changing a diagram.
//
// Each diagram plays a run: the steps light up in the order they run and end as succeeded, failed or skipped, then the
// run starts over. The colors come from Starlight's variables, so the diagrams follow the light and dark theme. With
// reduced motion, a diagram shows the end of the run without animating.

import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const output = join(dirname(fileURLToPath(import.meta.url)), '..', 'src', 'assets', 'control-flow');

const stepHeight = 40;
const columnWidth = 176;
const left = 8;
const slot = 1.1; // seconds per step
const runTime = 0.8; // seconds a step shows as running
const lead = 0.6; // seconds before the first step
const hold = 2.8; // seconds the end of the run stays

/**
 * A step: `col` and `row` place it, `slot` is its position in the run (none for a step that isn't part of the run),
 * `state` is how it ends: success, fail, skip or out (not part of the run). `tag` is shown above it, `note` below it
 * at the end of the run.
 */
const diagrams = [
	{
		name: 'conditions',
		title: 'Publish is skipped because its condition is false, and Report, which depends on it, still runs',
		rows: [{ command: 'baun Report' }],
		steps: [
			{ id: 'Pack', col: 0, row: 0, slot: 0, state: 'success' },
			{ id: 'Publish', col: 1, row: 0, slot: 1, state: 'skip', tag: 'OnlyWhen(IsRunning)', note: 'condition is false' },
			{ id: 'Report', col: 2, row: 0, slot: 2, state: 'success' },
		],
		edges: [
			{ from: 'Pack', to: 'Publish', kind: 'dependency' },
			{ from: 'Publish', to: 'Report', kind: 'dependency' },
		],
	},
	{
		name: 'ordering',
		title: 'baun Restore runs only Restore, while baun Restore Clean runs Clean first',
		rows: [{ command: 'baun Restore' }, { command: 'baun Restore Clean' }],
		steps: [
			{ id: 'Clean', key: 'Clean1', col: 0, row: 0, state: 'out', note: 'not part of the run' },
			{ id: 'Restore', key: 'Restore1', col: 1, row: 0, slot: 0, state: 'success', tag: 'After(Clean)' },
			{ id: 'Clean', key: 'Clean2', col: 0, row: 1, slot: 0, state: 'success' },
			{ id: 'Restore', key: 'Restore2', col: 1, row: 1, slot: 1, state: 'success', tag: 'After(Clean)' },
		],
		edges: [
			{ from: 'Clean1', to: 'Restore1', kind: 'order', inactive: true },
			{ from: 'Clean2', to: 'Restore2', kind: 'order' },
		],
	},
	{
		name: 'triggers',
		title: 'Publish triggers Notify, which runs after it',
		rows: [{ command: 'baun Publish' }],
		steps: [
			{ id: 'Pack', col: 0, row: 0, slot: 0, state: 'success' },
			{ id: 'Publish', col: 1, row: 0, slot: 1, state: 'success', tag: 'Triggers(Notify)' },
			{ id: 'Notify', col: 2, row: 0, slot: 2, state: 'success', note: 'triggered' },
		],
		edges: [
			{ from: 'Pack', to: 'Publish', kind: 'dependency' },
			{ from: 'Publish', to: 'Notify', kind: 'trigger' },
		],
	},
	{
		name: 'proceed-after-failure',
		title: 'Lint fails and the build goes on: Compile and Test run, Report, which depends on Lint, is skipped',
		rows: [{ command: 'baun Report' }, {}],
		steps: [
			{ id: 'Lint', col: 0, row: 0, slot: 0, state: 'fail', tag: 'ProceedAfterFailure()' },
			{ id: 'Compile', col: 0, row: 1, slot: 1, state: 'success' },
			{ id: 'Test', col: 1, row: 1, slot: 2, state: 'success' },
			{ id: 'Report', col: 2, row: 0, slot: 3, state: 'skip', note: 'a dependency failed' },
		],
		edges: [
			{ from: 'Lint', to: 'Report', kind: 'dependency' },
			{ from: 'Compile', to: 'Test', kind: 'dependency' },
			{ from: 'Test', to: 'Report', kind: 'dependency' },
		],
	},
	{
		name: 'always-runs',
		title: 'Test fails, so Pack is skipped, but UploadLogs runs anyway',
		rows: [{ command: 'baun Pack UploadLogs' }, {}],
		steps: [
			{ id: 'Test', col: 0, row: 0, slot: 0, state: 'fail' },
			{ id: 'Pack', col: 1, row: 0, slot: 1, state: 'skip', note: 'a step failed' },
			{ id: 'UploadLogs', col: 2, row: 1, slot: 2, state: 'success', tag: 'AlwaysRuns()' },
		],
		edges: [
			{ from: 'Test', to: 'Pack', kind: 'dependency' },
			{ from: 'Test', to: 'UploadLogs', kind: 'order' },
		],
	},
	{
		name: 'skip',
		title: 'baun Pack --skip Test skips Test, and Pack still runs',
		rows: [{ command: 'baun Pack --skip Test' }],
		steps: [
			{ id: 'Compile', col: 0, row: 0, slot: 0, state: 'success' },
			{ id: 'Test', col: 1, row: 0, slot: 1, state: 'skip', note: 'skipped with --skip' },
			{ id: 'Pack', col: 2, row: 0, slot: 2, state: 'success' },
		],
		edges: [
			{ from: 'Compile', to: 'Test', kind: 'dependency' },
			{ from: 'Test', to: 'Pack', kind: 'dependency' },
		],
	},
];

const escape = text => text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
const round = value => Math.round(value * 100) / 100;
const textWidth = text => text.length * 8.2;

function render(diagram) {
	const id = `cf-${diagram.name}`;
	const slots = Math.max(...diagram.steps.map(step => step.slot ?? 0)) + 1;
	const duration = lead + slots * slot + hold;
	const at = seconds => round((seconds / duration) * 100);
	const fadeOut = 92;
	const reset = 97;

	// Each row has a command line above it, a tag line, the steps and a note line.
	const rowTops = [];
	let y = 4;
	for (const row of diagram.rows) {
		rowTops.push(y);
		y += (row.command ? 26 : 0) + 22 + stepHeight + 40;
	}
	const height = y - 22;

	const steps = new Map();
	for (const step of diagram.steps) {
		const row = diagram.rows[step.row];
		const width = Math.max(96, Math.ceil(textWidth(step.id) + 52));
		const x = left + step.col * columnWidth;
		const top = rowTops[step.row] + (row.command ? 26 : 0) + 22;
		steps.set(step.key ?? step.id, { ...step, x, y: top, width });
	}
	const width = Math.max(...[...steps.values()].map(step => step.x + step.width)) + left;

	const keyframes = [];
	const parts = [];
	const animate = (name, frames) => {
		keyframes.push(`@keyframes ${id}-${name}{${frames}}`);
		return `${id}-${name}`;
	};

	// Commands
	diagram.rows.forEach((row, index) => {
		if (row.command) {
			parts.push(`<text class="cf-command" x="${left}" y="${rowTops[index] + 14}"><tspan class="cf-prompt">$</tspan> ${escape(row.command)}</text>`);
		}
	});

	// Edges, below the steps
	diagram.edges.forEach((edge, index) => {
		const from = steps.get(edge.from);
		const to = steps.get(edge.to);
		const x1 = from.x + from.width, y1 = from.y + stepHeight / 2;
		const x2 = to.x - 3, y2 = to.y + stepHeight / 2;
		const bend = Math.max(30, (x2 - x1) / 2);
		// An edge to a lower row leaves the bottom of its step and turns into the row, so it passes below the notes of
		// the steps in between.
		const cx = from.x + from.width / 2, r = 14;
		const d = y1 === y2 ? `M${x1},${y1} L${x2},${y2}`
			: y2 > y1 ? `M${cx},${from.y + stepHeight} L${cx},${y2 - r} Q${cx},${y2} ${cx + r},${y2} L${x2},${y2}`
			: `M${x1},${y1} C${x1 + bend},${y1} ${x2 - bend},${y2} ${x2},${y2}`;
		parts.push(`<path class="cf-edge cf-${edge.kind}" d="${d}" marker-end="url(#${id}-arrow)"/>`);

		if (edge.inactive || to.slot === undefined) return;

		const start = lead + to.slot * slot;
		const name = animate(`e${index}`,
			`0%,${at(start - 0.35)}%{opacity:0;stroke-dashoffset:1}` +
			`${at(start)}%,${fadeOut}%{opacity:1;stroke-dashoffset:0}` +
			`${reset}%,100%{opacity:0;stroke-dashoffset:0}`);
		const draw = edge.kind === 'dependency' ? ' pathLength="1"' : '';
		parts.push(`<path class="cf-edge cf-${edge.kind} cf-active${draw ? ' cf-draw' : ''}" d="${d}"${draw} marker-end="url(#${id}-arrow-active)" style="animation-name:${name}"/>`);
	});

	// Steps
	diagram.steps.forEach((step, index) => {
		const { x, y: top, width: w } = steps.get(step.key ?? step.id);
		const box = `x="${x}" y="${top}" width="${w}" height="${stepHeight}" rx="8"`;
		const label = `x="${x + 14}" y="${top + stepHeight / 2 + 5}"`;
		const icon = { success: '✓', fail: '✗', skip: '–' }[step.state];
		parts.push(`<g class="cf-step">`);

		if (step.tag) {
			parts.push(`<text class="cf-tag" x="${x + 2}" y="${top - 8}">.${escape(step.tag)}</text>`);
		}

		if (step.state === 'out') {
			parts.push(`<rect class="cf-box cf-out" ${box}/>`);
			parts.push(`<text class="cf-label cf-faint" ${label}>${escape(step.id)}</text>`);
			if (step.note) parts.push(`<text class="cf-note" x="${x + 2}" y="${top + stepHeight + 18}">${escape(step.note)}</text>`);
			parts.push(`</g>`);
			return;
		}

		const start = lead + step.slot * slot;
		const end = start + runTime;
		const run = animate(`r${index}`,
			`0%,${at(start)}%{opacity:0}${at(start + 0.1)}%,${at(end - 0.1)}%{opacity:1}${at(end)}%,100%{opacity:0}`);
		const done = animate(`d${index}`,
			`0%,${at(end - 0.1)}%{opacity:0}${at(end)}%,${fadeOut}%{opacity:1}${reset}%,100%{opacity:0}`);
		const dim = animate(`l${index}`,
			`0%,${at(end - 0.1)}%{opacity:1}${at(end)}%,${fadeOut}%{opacity:${step.state === 'skip' ? 0.45 : 1}}${reset}%,100%{opacity:1}`);

		parts.push(`<rect class="cf-box" ${box}/>`);
		parts.push(`<rect class="cf-box cf-${step.state} cf-done" ${box} style="animation-name:${done}"/>`);
		parts.push(`<rect class="cf-box cf-running cf-run" ${box} style="animation-name:${run}"/>`);
		parts.push(`<text class="cf-label cf-label-${step.state}" ${label} style="animation-name:${dim}">${escape(step.id)}</text>`);
		parts.push(`<text class="cf-icon cf-${step.state} cf-done" x="${x + w - 14}" y="${top + stepHeight / 2 + 5}" text-anchor="end" style="animation-name:${done}">${icon}</text>`);
		parts.push(`<circle class="cf-spinner cf-run" cx="${x + w - 19}" cy="${top + stepHeight / 2}" r="5" pathLength="1" style="animation-name:${run}"/>`);

		if (step.note) {
			parts.push(`<text class="cf-note cf-done" x="${x + 2}" y="${top + stepHeight + 18}" style="animation-name:${done}">${escape(step.note)}</text>`);
		}

		parts.push(`</g>`);
	});

	// The styles are scoped to the diagram, because an inlined SVG shares its page's styles.
	const s = `#${id}`;
	const style = [
		`${s}{font-family:var(--__sl-font);font-size:14px;overflow:visible}`,
		`${s} .cf-command{font-family:var(--__sl-font-mono);font-size:13px;fill:var(--sl-color-gray-2)}`,
		`${s} .cf-prompt{fill:var(--sl-color-accent)}`,
		`${s} .cf-tag{font-family:var(--__sl-font-mono);font-size:12px;fill:var(--sl-color-accent)}`,
		`${s} .cf-note{font-size:12px;fill:var(--sl-color-gray-3)}`,
		`${s} .cf-box{fill:var(--sl-color-gray-6);stroke:var(--sl-color-gray-5);stroke-width:1}`,
		`${s} .cf-box.cf-out{fill:none;stroke:var(--sl-color-gray-4);stroke-dasharray:4 4}`,
		`${s} .cf-box.cf-running{fill:var(--sl-color-accent-low);stroke:var(--sl-color-accent);stroke-width:2}`,
		`${s} .cf-box.cf-success{stroke:var(--sl-color-accent);stroke-width:1.5}`,
		`${s} .cf-box.cf-fail{fill:var(--sl-color-red-low);stroke:var(--sl-color-red);stroke-width:1.5}`,
		`${s} .cf-box.cf-skip{fill:var(--sl-color-bg);stroke:var(--sl-color-gray-4);stroke-dasharray:4 4}`,
		`${s} .cf-label{fill:var(--sl-color-white)}`,
		`${s} .cf-faint{fill:var(--sl-color-gray-3)}`,
		`${s} .cf-icon{font-size:15px}`,
		`${s} .cf-icon.cf-success{fill:var(--sl-color-accent)}`,
		`${s} .cf-icon.cf-fail{fill:var(--sl-color-red)}`,
		`${s} .cf-icon.cf-skip{fill:var(--sl-color-gray-3)}`,
		`${s} .cf-spinner{fill:none;stroke:var(--sl-color-accent);stroke-width:2;stroke-linecap:round;stroke-dasharray:.6 .4;transform-box:fill-box;transform-origin:center}`,
		`${s} .cf-edge{fill:none;stroke:var(--sl-color-gray-4);stroke-width:1.5}`,
		`${s} .cf-edge.cf-order{stroke-dasharray:6 4}`,
		`${s} .cf-edge.cf-trigger{stroke-dasharray:1 5;stroke-linecap:round;stroke-width:2}`,
		`${s} .cf-edge.cf-active{stroke:var(--sl-color-accent);stroke-width:2}`,
		`${s} .cf-edge.cf-draw{stroke-dasharray:1 1}`,
		`${s} .cf-arrow{fill:var(--sl-color-gray-4)}`,
		`${s} .cf-arrow-active{fill:var(--sl-color-accent)}`,
		// Without animations, the diagram shows the end of the run.
		`${s} .cf-run{opacity:0}`,
		`${s} :is(.cf-run,.cf-done,.cf-active,.cf-label){animation-duration:${round(duration)}s;animation-iteration-count:infinite;animation-timing-function:ease-in-out}`,
		`@keyframes cf-spin{to{transform:rotate(1turn)}}`,
		...keyframes,
		`@media (prefers-reduced-motion:reduce){${s} *{animation:none!important}${s} .cf-label-skip{opacity:.45}}`,
	];

	// The spinner fades in and out with its step while it turns, so it gets both animations.
	const svg = [
		`<svg id="${id}" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${round(width)} ${height}" width="${round(width)}" height="${height}" role="img" aria-label="${escape(diagram.title)}">`,
		`<title>${escape(diagram.title)}</title>`,
		`<style>${style.join('')}</style>`,
		`<defs>`,
		`<marker id="${id}-arrow" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse"><path class="cf-arrow" d="M0,0 L10,5 L0,10 z"/></marker>`,
		`<marker id="${id}-arrow-active" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse"><path class="cf-arrow-active" d="M0,0 L10,5 L0,10 z"/></marker>`,
		`</defs>`,
		...parts.map(part => part.replace(
			/<circle class="cf-spinner cf-run"([^>]*) style="animation-name:([^"]+)"\/>/,
			(_, attributes, name) => `<circle class="cf-spinner cf-run"${attributes} style="animation:${round(duration)}s ease-in-out infinite ${name}, .8s linear infinite cf-spin"/>`)),
		`</svg>`,
		'',
	].join('\n');

	return svg;
}

mkdirSync(output, { recursive: true });

for (const diagram of diagrams) {
	writeFileSync(join(output, `${diagram.name}.svg`), render(diagram));
	console.log(`Wrote ${diagram.name}.svg`);
}
