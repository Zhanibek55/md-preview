# Warm-up

Seeds the V8 code cache so the first real diagram is fast. Covers the heaviest scripts:
Mermaid (flowchart + sequence), KaTeX math, and highlight.js.

```mermaid
flowchart LR
    A[Start] --> B{Decision}
    B -->|Yes| C[Do it]
    B -->|No| D[Skip]
    C --> E[End]
    D --> E
```

```mermaid
sequenceDiagram
    participant A
    participant B
    A->>B: request
    B-->>A: response
```

```mermaid
classDiagram
    class Handler
    Handler : +DoPreview()
```

```mermaid
stateDiagram-v2
    [*] --> Idle
    Idle --> Rendering
    Rendering --> [*]
```

```mermaid
pie
    "A" : 40
    "B" : 60
```

```mermaid
gantt
    title Warm
    section S
    task : 2026-01-01, 1d
```

Inline math $e^{i\pi} + 1 = 0$ and a display block:

$$
\sum_{k=1}^{n} k = \frac{n(n+1)}{2}
$$

```python
def warm(x: int) -> int:
    return sum(range(x))
```

```javascript
const warm = (x) => [...Array(x).keys()].reduce((a, b) => a + b, 0);
```
