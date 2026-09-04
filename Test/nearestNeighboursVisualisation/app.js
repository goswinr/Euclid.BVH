import { createScene } from "./dist/Visualisation.js";

const svg = document.querySelector("#tree");
const lineOutput = document.querySelector("#line");
const summary = document.querySelector("#summary");
const countInput = document.querySelector("#count");
const previous = document.querySelector("#previous");
const next = document.querySelector("#next");
let seed = Date.now();
let lineIndex = 0;
let scene;

const element = (name, attributes) => {
  const item = document.createElementNS("http://www.w3.org/2000/svg", name);
  Object.entries(attributes).forEach(([key, value]) => item.setAttribute(key, value));
  return item;
};

function render() {
  const count = Math.max(1, Math.min(10, Number.parseInt(countInput.value, 10) || 1));
  countInput.value = count;
  scene = createScene(seed, lineIndex, count);
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
  summary.value = `${scene.Visited.length} BRects tested · distances ${scene.Neighbors.map((neighbor) => neighbor.Distance.toFixed(2)).join(", ")}`;
  previous.disabled = lineIndex === 0;
  next.disabled = lineIndex === scene.Lines.length - 1;
}

previous.addEventListener("click", () => { lineIndex--; render(); });
next.addEventListener("click", () => { lineIndex++; render(); });
countInput.addEventListener("change", render);
document.querySelector("#regenerate").addEventListener("click", () => {
  seed++;
  lineIndex = 0;
  render();
});

render();
