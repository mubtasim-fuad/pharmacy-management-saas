# Angular frontend

Run `npm ci && npm start` from this directory. Open `http://localhost:4200`. For a static build, use `npm run build`; the output is `dist/pharmacy/browser`.

`public/config.js` sets the HTTPS API origin at runtime. An empty value enables the browser-only sample demo on a hosted site; on localhost it defaults to `http://localhost:5075`. To use real accounts online, set `window.PHARMACY_API_URL = "https://your-api-host";` and redeploy. The API must allow the frontend origin through `Cors__AllowedOrigins`.

For Vercel, import this GitHub repository, set **Root Directory** to `frontend`, and deploy. `vercel.json` supplies the build command, output folder and SPA fallback.
