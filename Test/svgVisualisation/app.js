import { createScene } from "./dist/Visualisation.js";

const svg = document.querySelector("#tree");
const levelOutput = document.querySelector("#level");
const previous = document.querySelector("#previous");
const next = document.querySelector("#next");
let seed = Date.now();
let scene = createScene(seed);
let level = 0;

const element = (name, attributes) => {
  const item = document.createElementNS("http://www.w3.org/2000/svg", name);
  Object.entries(attributes).forEach(([key, value]) => item.setAttribute(key, value));
  return item;
};

function render() {
  svg.replaceChildren();
  scene.Lines.forEach((line) => svg.append(element("line", { class: "line", x1: line.X1, y1: line.Y1, x2: line.X2, y2: line.Y2 })));
  scene.Levels[level].forEach((rect) => svg.append(element("rect", { class: "node", x: rect.MinX, y: rect.MinY, width: rect.MaxX - rect.MinX, height: rect.MaxY - rect.MinY })));
  levelOutput.value = `Depth ${level + 1}/${scene.Levels.length}`;
  previous.disabled = level === 0;
  next.disabled = level === scene.Levels.length - 1;
}

previous.addEventListener("click", () => { level--; render(); });
next.addEventListener("click", () => { level++; render(); });
document.querySelector("#regenerate").addEventListener("click", () => {
  scene = createScene(++seed);
  level = 0;
  render();
});

render();
