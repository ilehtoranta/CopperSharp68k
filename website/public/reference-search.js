(() => {
  const input = document.querySelector("#sdk-search");
  const filter = document.querySelector("#sdk-kind-filter");
  const results = document.querySelector("#sdk-search-results");
  const browser = document.querySelector("#sdk-module-browser");
  const status = document.querySelector("#sdk-search-status");
  if (!input || !filter || !results || !browser || !status) return;

  const projectBase = window.location.pathname.startsWith("/CopperSharp68k/")
    ? "/CopperSharp68k"
    : "";
  let entries = [];
  let activeResult = -1;

  const typeKinds = new Set(["class", "static class", "struct", "ref struct", "enum", "interface", "delegate"]);
  const fieldKinds = new Set(["field", "constant", "enum value"]);

  function matchesFilter(entry, value) {
    if (!value) return true;
    if (value === "type") return typeKinds.has(entry.kind);
    if (value === "field") return fieldKinds.has(entry.kind);
    return entry.kind === value;
  }

  function score(entry, query, terms) {
    const name = entry.name.toLowerCase();
    const signature = entry.signature.toLowerCase();
    const haystack = [entry.name, entry.kind, entry.module, entry.type, entry.signature, entry.summary, entry.library]
      .filter(Boolean)
      .join(" ")
      .toLowerCase();
    if (!terms.every((term) => haystack.includes(term))) return -1;
    if (name === query) return 1000;
    if (name.startsWith(query)) return 800;
    if (name.includes(query)) return 650;
    if ((entry.library || "").toLowerCase() === query) return 600;
    if (signature.includes(query)) return 500;
    return 200 + terms.reduce((total, term) => total + (name.includes(term) ? 30 : 0), 0);
  }

  function addText(parent, tag, className, value) {
    const element = document.createElement(tag);
    if (className) element.className = className;
    element.textContent = value;
    parent.append(element);
    return element;
  }

  function render() {
    const query = input.value.trim().toLowerCase();
    const terms = query.split(/[^a-z0-9_]+/).filter(Boolean);
    const kind = filter.value;
    const params = new URLSearchParams();
    if (input.value.trim()) params.set("q", input.value.trim());
    if (kind) params.set("kind", kind);
    history.replaceState(null, "", `${window.location.pathname}${params.size ? `?${params}` : ""}`);

    if (!query && !kind) {
      results.hidden = true;
      browser.hidden = false;
      results.replaceChildren();
      status.textContent = "Type a name, signature, module, or library.";
      return;
    }

    const matched = entries
      .filter((entry) => matchesFilter(entry, kind))
      .map((entry) => ({ entry, score: query ? score(entry, query, terms) : 100 }))
      .filter((result) => result.score >= 0)
      .sort((left, right) => right.score - left.score || left.entry.name.localeCompare(right.entry.name))
      .slice(0, 80);

    results.replaceChildren();
    for (const { entry } of matched) {
      const link = document.createElement("a");
      link.className = "reference-result";
      link.href = `${projectBase}${entry.href}`;
      const heading = document.createElement("span");
      heading.className = "reference-result-heading";
      addText(heading, "strong", "", entry.name);
      addText(heading, "i", "reference-kind", entry.kind);
      link.append(heading);
      addText(link, "code", "", entry.signature);
      if (entry.summary) addText(link, "p", "", entry.summary);
      addText(link, "small", "", [entry.module, entry.type, entry.library].filter(Boolean).join(" · "));
      results.append(link);
    }

    activeResult = -1;
    results.hidden = false;
    browser.hidden = true;
    status.textContent = matched.length === 80
      ? "Showing the first 80 matching declarations."
      : `${matched.length} matching declaration${matched.length === 1 ? "" : "s"}.`;
  }

  function moveSelection(direction) {
    const links = [...results.querySelectorAll("a")];
    if (!links.length) return;
    activeResult = (activeResult + direction + links.length) % links.length;
    links[activeResult].focus();
  }

  const initial = new URLSearchParams(window.location.search);
  input.value = initial.get("q") || "";
  filter.value = initial.get("kind") || "";

  fetch(`${projectBase}/reference-index.json`)
    .then((response) => {
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      return response.json();
    })
    .then((value) => {
      entries = value;
      render();
    })
    .catch(() => {
      status.textContent = "The search index could not be loaded. Browse the module list below.";
      browser.hidden = false;
    });

  input.addEventListener("input", render);
  filter.addEventListener("change", render);
  input.addEventListener("keydown", (event) => {
    if (event.key === "ArrowDown") {
      event.preventDefault();
      moveSelection(1);
    }
  });
  document.addEventListener("keydown", (event) => {
    if (event.key === "/" && document.activeElement !== input && !/INPUT|TEXTAREA|SELECT/.test(document.activeElement?.tagName || "")) {
      event.preventDefault();
      input.focus();
    }
    if (event.key === "Escape" && document.activeElement === input) {
      input.value = "";
      render();
      input.blur();
    }
    if (event.key === "ArrowDown" && document.activeElement?.classList.contains("reference-result")) {
      event.preventDefault();
      moveSelection(1);
    }
    if (event.key === "ArrowUp" && document.activeElement?.classList.contains("reference-result")) {
      event.preventDefault();
      moveSelection(-1);
    }
  });
})();
