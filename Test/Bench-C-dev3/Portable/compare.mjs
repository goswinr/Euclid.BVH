// Run prepared baseline/optimized artifacts sequentially, without competing benchmarks.
import { spawn } from 'node:child_process';
import { mkdirSync, writeFileSync, readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = fileURLToPath(new URL('../', import.meta.url));
const runtime = process.argv[2] ?? 'dotnet';
if (!['dotnet', 'js'].includes(runtime)) throw new Error('Expected dotnet or js');
const repeats = Number(process.argv[3] ?? 1);
if (!Number.isSafeInteger(repeats) || repeats < 1) throw new Error('Expected a positive repeat count');
const candidate = process.argv[4] ?? 'optimized';
if (!/^[a-z][a-z0-9-]*$/.test(candidate) || candidate === 'baseline') throw new Error('Invalid candidate name');
const reportOnly = process.argv.includes('--report-only');
const buildOnly = process.argv.includes('--build-only');
const caseCount = buildOnly ? 8 : 40;
const reports = path.join(root, 'measurements', candidate + (buildOnly ? '-build' : ''));
mkdirSync(reports, { recursive: true });

async function run(variant, repeat) {
    const reportPath = path.join(reports, `${runtime}-${variant}-${repeat}.csv`);
    if (reportOnly) return parse(readFileSync(reportPath, 'utf8'));
    const entry = path.join(root, 'dist', `${variant}-${runtime}`, runtime === 'dotnet' ? 'Portable.dll' : 'Program.js');
    const command = runtime === 'dotnet' ? 'dotnet' : process.execPath;
    const args = runtime === 'dotnet' ? [entry] : ['--expose-gc', entry];
    if (buildOnly) args.push('--build-only');
    console.log(`Starting ${runtime} ${variant}, repeat ${repeat}`);
    const child = spawn(command, args, { cwd: root, stdio: ['ignore', 'pipe', 'inherit'] });
    let output = '';
    let lines = 0;
    child.stdout.on('data', chunk => {
        output += chunk.toString();
        const completed = Math.max(0, output.split('\n').length - 2);
        if (completed - lines >= (buildOnly ? 2 : 10)) {
            console.log(`  ${completed}/${caseCount} cases complete`);
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
        const [dimensions, distribution, count, queries, operation, batch, median, min, max, bytes, checksum] = line.split(',');
        return { dimensions, distribution, count, queries, operation, median: Number(median), bytes: Number(bytes), checksum };
    });
}

const all = { baseline: [], [candidate]: [] };
for (let repeat = 1; repeat <= repeats; repeat++) {
    for (const variant of repeat % 2 ? ['baseline', candidate] : [candidate, 'baseline']) {
        all[variant].push(await run(variant, repeat));
    }
}
const median = xs => {
    const sorted = [...xs].sort((a, b) => a - b);
    const n = sorted.length;
    return n % 2 ? sorted[(n - 1) / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2;
};
console.log(`dim,distribution,count,operation,baseline_ns,${candidate}_ns,speedup`);
const groups = new Map();
for (let i = 0; i < all.baseline[0].length; i++) {
    const row = all.baseline[0][i];
    const before = median(all.baseline.map(rows => rows[i].median));
    const after = median(all[candidate].map(rows => rows[i].median));
    const key = r => `${r.dimensions}/${r.distribution}/${r.count}/${r.queries}/${r.operation}`;
    for (const rows of [...all.baseline, ...all[candidate]]) {
        if (rows.length !== caseCount || key(rows[i]) !== key(row) || rows[i].checksum !== row.checksum) throw new Error('Case or checksum mismatch');
    }
    console.log(`${row.dimensions},${row.distribution},${row.count},${row.operation},${before.toFixed(1)},${after.toFixed(1)},${(before / after).toFixed(3)}`);
    const group = `${row.dimensions}D ${row.operation}`;
    if (!groups.has(group)) groups.set(group, []);
    groups.get(group).push(before / after);
}
console.log('\nGeometric mean speedup across distributions and sizes (range):');
for (const [group, ratios] of groups) {
    const mean = Math.exp(ratios.reduce((sum, x) => sum + Math.log(x), 0) / ratios.length);
    console.log(`${group}: ${mean.toFixed(3)}x (${Math.min(...ratios).toFixed(3)}-${Math.max(...ratios).toFixed(3)}x)`);
}
