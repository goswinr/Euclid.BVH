// Runs two prepared variants, baseline and optimized by default, one after the other, never concurrently,
// and reports the speedup per case.
// Usage: node compare.mjs <dotnet|js> [repeats] [--smoke] [--report-only] [--baseline=baseline] [--candidate=optimized] [--only=build,closest-pair-box]
import { spawn } from 'node:child_process';
import { mkdirSync, writeFileSync, readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = fileURLToPath(new URL('./', import.meta.url));
const runtime = process.argv[2] ?? 'dotnet';
if (!['dotnet', 'js'].includes(runtime)) throw new Error('Expected dotnet or js');
const repeats = Number(process.argv[3] ?? 2);
if (!Number.isSafeInteger(repeats) || repeats < 1) throw new Error('Expected a positive repeat count');
const reportOnly = process.argv.includes('--report-only');
const smoke = process.argv.includes('--smoke');
const option = name => process.argv.find(a => a.startsWith(`--${name}=`))?.slice(name.length + 3);
const base = option('baseline') ?? 'baseline';
const candidate = option('candidate') ?? 'optimized';
for (const name of [base, candidate]) if (!/^[a-z][a-z0-9-]*$/.test(name)) throw new Error(`Invalid variant name ${name}`);
if (base === candidate) throw new Error('The baseline and the candidate must differ');
const only = option('only');
const reportName = (base === 'baseline' ? '' : base + '-vs-') + candidate + (only ? '-' + only.replaceAll(',', '+') : '');
const reports = path.join(root, 'measurements' + (smoke ? '-smoke' : ''), reportName);
mkdirSync(reports, { recursive: true });

async function run(variant, repeat) {
    const reportPath = path.join(reports, `${runtime}-${variant}-${repeat}.csv`);
    if (reportOnly) return parse(readFileSync(reportPath, 'utf8'));
    const entry = path.join(root, 'dist', `${variant}-${runtime}`, runtime === 'dotnet' ? 'BenchQueries.dll' : 'Program.js');
    const command = runtime === 'dotnet' ? 'dotnet' : process.execPath;
    const args = runtime === 'dotnet' ? [entry] : ['--expose-gc', entry];
    if (smoke) args.push('--smoke');
    if (only) args.push(`--only=${only}`);
    console.log(`Starting ${runtime} ${variant}, repeat ${repeat}`);
    const child = spawn(command, args, { cwd: root, stdio: ['ignore', 'pipe', 'inherit'] });
    let output = '';
    let lines = 0;
    child.stdout.on('data', chunk => {
        output += chunk.toString();
        const completed = Math.max(0, output.split('\n').length - 2);
        if (completed - lines >= 16) {
            console.log(`  ${completed} cases complete`);
            lines = completed;
        }
    });
    await new Promise((resolve, reject) => {
        child.on('error', reject);
        child.on('close', code => code === 0 ? resolve() : reject(new Error(`Benchmark exited ${code}`)));
    });
    writeFileSync(reportPath, output);
    return parse(output);
}

function parse(output) {
    return output.trim().split(/\r?\n/).slice(1).map(line => {
        const [dimensions, distribution, count, operation, units, batch, median, min, max, bytes, checksum] = line.split(',');
        return { dimensions, distribution, count, operation, median: Number(median), bytes: Number(bytes), checksum };
    });
}

// Alternate the order, baseline first in odd repeats, so neither variant always runs on a warmer machine.
const all = { [base]: [], [candidate]: [] };
for (let repeat = 1; repeat <= repeats; repeat++) {
    for (const variant of repeat % 2 ? [base, candidate] : [candidate, base]) {
        all[variant].push(await run(variant, repeat));
    }
}

const median = xs => {
    const sorted = [...xs].sort((a, b) => a - b);
    const n = sorted.length;
    return n % 2 ? sorted[(n - 1) / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2;
};
const key = r => `${r.dimensions}/${r.distribution}/${r.count}/${r.operation}`;
const reference = all[base][0];
for (const rows of [...all[base], ...all[candidate]]) {
    if (rows.length !== reference.length) throw new Error('Case count mismatch');
    rows.forEach((row, i) => {
        if (key(row) !== key(reference[i])) throw new Error(`Case mismatch: ${key(row)}`);
        if (row.checksum !== reference[i].checksum) throw new Error(`Checksum mismatch: ${key(row)}`);
    });
}

const showBytes = runtime === 'dotnet';
console.log(`dim,distribution,count,operation,${base}_ns,${candidate}_ns,speedup${showBytes ? `,${base}_bytes,${candidate}_bytes` : ''}`);
const groups = new Map();
reference.forEach((row, i) => {
    const before = median(all[base].map(rows => rows[i].median));
    const after = median(all[candidate].map(rows => rows[i].median));
    const bytes = showBytes ? `,${median(all[base].map(rows => rows[i].bytes)).toFixed(0)},${median(all[candidate].map(rows => rows[i].bytes)).toFixed(0)}` : '';
    console.log(`${row.dimensions},${row.distribution},${row.count},${row.operation},${before.toFixed(1)},${after.toFixed(1)},${(before / after).toFixed(3)}${bytes}`);
    const group = `${row.dimensions}D ${row.operation}`;
    if (!groups.has(group)) groups.set(group, []);
    groups.get(group).push(before / after);
});
console.log('\nGeometric mean speedup across distributions and sizes (range):');
for (const [group, ratios] of groups) {
    const mean = Math.exp(ratios.reduce((sum, x) => sum + Math.log(x), 0) / ratios.length);
    console.log(`${group}: ${mean.toFixed(3)}x (${Math.min(...ratios).toFixed(3)}-${Math.max(...ratios).toFixed(3)}x)`);
}
