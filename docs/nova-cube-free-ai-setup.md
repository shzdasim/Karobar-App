# Nova and Cube — Free local AI setup

Nova and Cube use **Ollama**, which runs an open-weight language model on infrastructure you control. The Laravel application calls the Ollama HTTP API on your private server network. No OpenAI account, API key, subscription, or per-message AI charge is required.

> Compute is not literally cost-free: the server still needs RAM, CPU/GPU capacity, electricity, and disk space. The model is downloaded once and inference runs on your own machine.

## 1. Install Ollama on the application server

On a supported Linux server, install Ollama using its official installer:

\`\`\`bash
curl -fsSL https://ollama.com/install.sh | sh
\`\`\`

Make sure the Ollama service is running:

\`\`\`bash
sudo systemctl enable --now ollama
sudo systemctl status ollama
\`\`\`

If Ollama was installed using another method, start it using that installation's service manager.

## 2. Download the free model

The default model is \`qwen3.5:4b\`:

\`\`\`bash
ollama pull qwen3.5:4b
ollama run qwen3.5:4b
\`\`\`

The 4B model download is approximately 3.3–4.0 GB. If the server has limited memory, you can try the smaller \`qwen3.5:2b\` model by changing \`OLLAMA_MODEL\`. Smaller models are generally less capable.

## 3. Configure Laravel

Add these values to the server's real \`.env\` file:

\`\`\`dotenv
OLLAMA_BASE_URL=http://127.0.0.1:11434
OLLAMA_MODEL=qwen3.5:4b
OLLAMA_TIMEOUT=120
\`\`\`

If Laravel runs inside a container and Ollama runs outside it, \`127.0.0.1\` points to the Laravel container, not the host. Set \`OLLAMA_BASE_URL\` to the Ollama host's private network address or private Docker service name instead.

Then clear Laravel's cached configuration:

\`\`\`bash
php artisan config:clear
\`\`\`

## 4. Verify Ollama before opening the ERP

From the same machine/network namespace as the Laravel application:

\`\`\`bash
curl http://127.0.0.1:11434/api/chat \\
  -H 'Content-Type: application/json' \\
  -d '{
    "model": "qwen3.5:4b",
    "messages": [{"role": "user", "content": "Reply with: Ollama is ready"}],
    "stream": false
  }'
\`\`\`

The response should include \`message.content\`.

## 5. Network and privacy

- Keep Ollama's port \`11434\` private. Do not expose it directly to the public internet.
- Allow access only from the Laravel application host/container.
- Do not put Ollama credentials or private host details in Vite/client-side environment variables.
- Chat history is held in the browser session state by this widget; the controller does not save it to the Karobar database.
- The assistant is explanatory only. It cannot read live tenant records or create/modify invoices, payments, stock, or other ERP transactions.

## Troubleshooting

- **Connection refused / service unavailable:** check \`systemctl status ollama\`, the service logs, and \`OLLAMA_BASE_URL\`.
- **Model not found:** run \`ollama pull qwen3.5:4b\` on the Ollama host.
- **Slow responses:** use a smaller model, reduce server load, or provide more RAM/CPU/GPU capacity.
- **Container networking:** do not use \`127.0.0.1\` unless Ollama is in the same network namespace as PHP.
