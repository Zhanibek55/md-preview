# Rich features :sparkles:

Emoji shortcodes should render: :rocket: :tada: :white_check_mark: :warning: :bulb:
(ASCII smileys like :) must stay literal.)

## Code without a language tag

```
function autodetect(x) {
  const total = x.reduce((a, b) => a + b, 0);
  return total / x.length;
}
```

## Code with a language tag

```python
def mean(xs: list[float]) -> float:
    return sum(xs) / len(xs)
```

## Plain text block (must stay unhighlighted)

```text
This is prose, not code. It should not be colored like a program.
```

## Remote image (blocked by default)

![A remote banner](https://example.com/banner.png)

## Searchable content

The word preview appears here. Preview again, and preview a third time, so a find for
"preview" has several hits to cycle through.

## Heading four

Filler so the outline has enough entries to be useful. Filler filler filler.

## Heading five

More filler. The outline panel lists every heading on this page.

## Heading six

Last section. Filler filler filler filler filler filler filler filler filler filler.
