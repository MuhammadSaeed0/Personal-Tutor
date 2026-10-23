# Browser smoke test

Drives the running application in a real browser (Edge or Chrome, headless) through the main student journey:
register → onboarding → upload an Arabic PDF (generated on the fly) → processing timeline → reader →
text selection & highlight → in-book search → dashboard in Arabic (RTL) and English (LTR) → library →
flashcards → equation balancer → plan → mobile layout. Screenshots are written to `e2e/shots/`.

```bash
# with the API (http://localhost:5080) and `ng serve` (http://localhost:4200) running:
cd e2e
npm install
npm run journey                       # uses Microsoft Edge
BROWSER_CHANNEL=chrome npm run journey
RIWAQ_URL=http://localhost:5080 npm run journey   # against a published single-process build
```

The journey does not depend on AI: without a provider the tutor shows the "not configured" notice; with
`ClaudeCode` (your Claude account) or an API key it uses the live model.
