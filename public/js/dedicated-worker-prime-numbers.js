const getPrimeNumbers = (num = 5e7) => {
  const startTime = Date.now();
  const pn = [2];
  for (let n = 3; n <= num; n++) {
    const m = Math.sqrt(n);
    let isPrime = true;
    for (const p of pn) {
      if (p > m) break;
      if (n % p === 0) {
        isPrime = false;
        break;
      }
    }
    if (isPrime) pn.push(n);
  }
  return { length: pn.length, howLong: Date.now() - startTime };
};

// self 在 worker 裡是 DedicatedWorkerGlobalScope
self.onmessage = (event) => {
  if (event.data === "start") {
    postMessage(getPrimeNumbers());
  }
};
