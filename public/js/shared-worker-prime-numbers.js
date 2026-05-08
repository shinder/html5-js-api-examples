// 除錯： chrome://inspect/#workers

const pn = [2];
let runningTime;

// 載入時就算一次（兩個頁面共用同一份結果，只算這次）
const getPrimeNumbers = (num = 5e7) => {
  const startTime = Date.now();
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
  runningTime = Date.now() - startTime;
};
getPrimeNumbers();

// 每個連入的頁面會觸發一次 onconnect；event.ports[0] 是該連線的專屬 port
self.onconnect = (event) => {
  const port = event.ports[0];
  port.onmessage = (e) => {
    port.postMessage({ primeNumbers: pn, runningTime, action: e.data });
  };
};
