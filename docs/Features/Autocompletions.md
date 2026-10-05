# SwarmUI Autocompletions Engine

When you're typing into a prompt box within Swarm, the **autocompletions engine** is looking for ways to help you.

### Prompt Syntax

The first way it will try to help you is with regards to advanced prompt syntax, as documented in [Basic Usage](/docs/Basic%20Usage.md). As soon as you type the `<` symbol, you'll see suggestions for prompt syntax options:

![image](/docs/images/autocompletions.png)

- You can start typing the name of one to narrow down to just that option.
- You can hit *tab* or click an option to immediately complete it.
- For syntax features with more options/configuration, it will complete to eg `<random:` with a colon on the end, and the replace the completions with documentation on what options you have available.

### Word Lists

If you want to have autocompletions for word lists (*such as anime booru tags*), you can! You just need to set it up first:

- Find a word-list file. There are several here you can use: <https://github.com/DominikDoom/a1111-sd-webui-tagcomplete/tree/main/tags>.
    - Also several matched sets for recent major anime models here: <https://github.com/BetaDoggo/danbooru-tag-list/releases>.
    - Any `.csv` will do (if the word is the first entry per row). Optionally second column can be an ID number 0-5 for unique colorations to distinguish categories.
    - or `.txt` files (for newline-separated wordlists, allowing `#` to mark comments).
- Save the file into `SwarmUI/Data/Autocompletions`.
- Restart swarm or reload parameter values if necessary.
- Go to `User` -> `User Settings`
- find the option `AutoCompletionsSource` and select your word list file of choice.
- Go back to the generate tab, and start typing! Words will pop up and are tab completable or clickable.

### Chants

Chants are named prompt snippets. Select a JSON file under **User → User Settings → AutoComplete → Chant Source** to enable them independently of your word list. Files belong in `Data/Autocompletions`; restart Swarm or reload parameter values after adding or editing a file.

The JSON format is an array of entries:

```json
[
    {
        "name": "Basic-NegativePrompt",
        "terms": "Basic,Negative,Low,Quality",
        "content": "(worst quality, low quality, normal quality)",
        "color": 3
    }
]
```

Type `<chant:` in either the positive or negative prompt to autocomplete names from your selected file. Suggestions also match the optional comma-separated `terms`. For example, `<chant:Basic-NegativePrompt>` expands to `(worst quality, low quality, normal quality)` during generation. Names are case-insensitive and must be unique within the file. Content is inserted as written, including parentheses and line breaks. A missing chant leaves the tag intact and records a parser warning. Leave **Chant Source** empty to disable chants.
