import { createScene } from "./dist/Visualisation.js";

const svg = document.querySelector("#tree");
const lineOutput = document.querySelector("#line");
const summary = document.querySelector("#summary");
const countInput = document.querySelector("#count");
const lineCountInput = document.querySelector("#line-count");
const previous = document.querySelector("#previous");
const next = document.querySelector("#next");
let seed = Date.now();
let lineIndex = 0;
let scene;
let renderId = 0;
let performanceWorker;
let inputTimer;

const element = (name, attributes) => {
  const item = document.createElementNS("http://www.w3.org/2000/svg", name);
  Object.entries(attributes).forEach(([key, value]) => item.setAttribute(key, value));
  return item;
};

function render() {
  const currentRenderId = ++renderId;
  const count = Math.max(1, Math.min(10, Number.parseInt(countInput.value, 10) || 1));
  const lineCount = Math.max(20, Math.min(20000, Number.parseInt(lineCountInput.value, 10) || 100));
  countInput.value = count;
  lineIndex = Math.max(0, Math.min(lineCount - 1, lineIndex));
  scene = createScene(seed, lineIndex, count, lineCount);
  const neighbors = new Map(scene.Neighbors.map((neighbor, rank) => [neighbor.Index, rank + 1]));

  svg.replaceChildren();
  scene.Visited.forEach((rect, index) => svg.append(element("rect", {
    class: "node",
    x: rect.MinX,
    y: rect.MinY,
    width: rect.MaxX - rect.MinX,
    height: rect.MaxY - rect.MinY,
    opacity: 0.25 + 0.65 * (index + 1) / scene.Visited.length
  })));
  scene.Lines.forEach((line, index) => {
    const rank = neighbors.get(index);
    const className = index === scene.QueryIndex ? "line query" : rank ? `line neighbor rank-${rank}` : "line";
    svg.append(element("line", { class: className, x1: line.X1, y1: line.Y1, x2: line.X2, y2: line.Y2 }));
  });

  lineOutput.value = `Line ${lineIndex + 1}/${scene.Lines.length}`;
  summary.value = `${scene.Visited.length} BRects tested · measuring performance…`;
  previous.disabled = lineIndex === 0;
  next.disabled = lineIndex === scene.Lines.length - 1;

  performanceWorker?.terminate();
  performanceWorker = new Worker(new URL("./performanceWorker.js", import.meta.url), { type: "module" });
  performanceWorker.addEventListener("message", ({ data }) => {
    if (currentRenderId !== renderId || data.renderId !== currentRenderId) return;
    if (data.error) {
      summary.value = `${scene.Visited.length} BRects tested · performance measurement failed`;
      return;
    }
    const performance = data.performance;
    const speedup = performance.BruteForceMilliseconds / performance.BvhMilliseconds;
    summary.value = `${scene.Visited.length} BRects tested · BVH ${performance.BvhMilliseconds.toFixed(3)} ms · brute force ${performance.BruteForceMilliseconds.toFixed(3)} ms · ${speedup.toFixed(1)}× (${performance.Iterations} runs)`;
  });
  performanceWorker.postMessage({ renderId: currentRenderId, seed, lineIndex, lineCount });
}

function renderLineCount() {
  clearTimeout(inputTimer);
  inputTimer = setTimeout(render, 150);
}

previous.addEventListener("click", () => { lineIndex--; render(); });
next.addEventListener("click", () => { lineIndex++; render(); });
countInput.addEventListener("change", render);
lineCountInput.addEventListener("input", () => {
  const lineCount = Number.parseInt(lineCountInput.value, 10);
  if (lineCount >= 20 && lineCount <= 20000) renderLineCount();
});
lineCountInput.addEventListener("change", () => {
  clearTimeout(inputTimer);
  lineCountInput.value = Math.max(20, Math.min(20000, Number.parseInt(lineCountInput.value, 10) || 100));
  render();
});
document.querySelector("#regenerate").addEventListener("click", () => {
  seed++;
  lineIndex = 0;
  render();
});

render();
