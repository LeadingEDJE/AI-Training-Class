# Tips on Model Selection

In general I tend to use heavy models like the Opus series for planning and then switch to cheaper models for implementation.

For trivial tasks you may just want to work with a mid or even low-level model entirely.

I will usually keep models on their default effort level because they're typically optimized for that level for cost and speed on their model providers, but execution doesn't usually need high effort while complex plans can benefit from high or beyond thinking levels.

Try to avoid large conversations that span multiple topics as this can lead to context bloat, unnecessary token usage and costs, additional chances of hallucinations, etc.

Instead, try to make your models output a usable artifact such as architectural decision records, specs / plans, or something else, then reference that artifact in a new conversation as needed.
