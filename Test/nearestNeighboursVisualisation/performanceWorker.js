import { measurePerformance } from "./dist/Visualisation.js";

self.addEventListener("message", ({ data }) => {
  try {
    const performance = measurePerformance(data.seed, data.lineIndex, data.lineCount);
    self.postMessage({ renderId: data.renderId, performance });
  } catch (error) {
    self.postMessage({ renderId: data.renderId, error: String(error) });
  }
});
