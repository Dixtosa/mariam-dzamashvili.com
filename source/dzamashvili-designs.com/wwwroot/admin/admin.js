"use strict";

const $ = selector => document.querySelector(selector);

let state = null;            // GET /api/admin/state
let selected = undefined;    // name of the project in the editor, null while creating one
let formDirty = false;
let imageStamp = Date.now(); // cache buster: deleting a slide renumbers the ones after it
const drafts = {};           // unsaved copies of the lists below, by key

const listUrls = {
    homepageProjects: "/homepage-projects",
    projectsGrid: "/projects-grid",
    promotionalMessages: "/promotional-messages",
};

function el(tag, props = {}, ...children) {
    const node = Object.assign(document.createElement(tag), props);
    node.append(...children);
    return node;
}

function iconButton(text, title, disabled, onClick) {
    const button = el("button", { type: "button", className: "icon", textContent: text, title, disabled });
    button.setAttribute("aria-label", title);
    button.addEventListener("click", onClick);
    return button;
}

async function api(method, url, body) {
    const options = { method, headers: {} };
    if (body instanceof FormData) {
        options.body = body;
    } else if (body !== undefined) {
        options.headers["Content-Type"] = "application/json";
        options.body = JSON.stringify(body);
    }

    const response = await fetch("/api/admin" + url, options);
    if (response.status === 401) {
        showLogin();
        throw new Error("Please log in again.");
    }
    if (!response.ok) {
        let message = `${response.status} ${response.statusText}`;
        try {
            const problem = await response.json();
            message = problem.detail || problem.title || message;
        } catch { }
        throw new Error(message);
    }
    return response.status === 204 ? null : response.json();
}

let toastTimer;
function toast(message, isError = false) {
    const node = $("#toast");
    node.textContent = message;
    node.className = "toast show" + (isError ? " error" : "");
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => node.className = "toast", isError ? 6000 : 2500);
}

// Runs a UI action, disabling its button meanwhile and reporting errors.
async function run(action, button) {
    if (button) button.disabled = true;
    try {
        await action();
    } catch (error) {
        toast(error.message, true);
    } finally {
        if (button) button.disabled = false;
        updateSaveButtons();
    }
}

const projectByName = name => state.projects.find(p => p.name === name);
const categoryTitle = id => state.categories.find(c => c.id === id)?.title ?? id;

/* Login */

function showLogin() {
    $("#app").hidden = true;
    $("#login").hidden = false;
    $("#password").focus();
}

async function start() {
    try {
        state = await api("GET", "/state");
    } catch (error) {
        if ($("#login").hidden) toast(error.message, true);
        return;
    }
    $("#login").hidden = true;
    $("#app").hidden = false;
    renderAll();
}

async function reload() {
    state = await api("GET", "/state");
    renderAll();
}

function renderAll() {
    // Drafts may still name projects that were renamed or deleted since.
    for (const key of ["homepageProjects", "projectsGrid"])
        if (drafts[key]) drafts[key] = drafts[key].filter(projectByName);

    renderProjectList();
    renderList("homepageProjects");
    renderList("projectsGrid");
    renderMessages();

    // Saving a list changes the open project's checkboxes.
    const form = $("#editor form");
    const project = selected && projectByName(selected);
    if (form && project) {
        form.elements.showInGrid.checked = project.showInGrid;
        form.elements.showOnHomepage.checked = project.showOnHomepage;
    }
}

/* Projects tab */

function renderProjectList() {
    const query = $("#project-search").value.trim().toLowerCase();
    const groups = state.categories.map(category => {
        const projects = state.projects.filter(p => p.category === category.id && p.name.toLowerCase().includes(query));
        if (!projects.length) return "";

        return el("div", { className: "group" },
            el("h4", { textContent: `${category.title} · ${projects.length}` }),
            ...projects.map(project => {
                const item = el("button", { type: "button", className: "project-item" + (project.name === selected ? " active" : "") },
                    el("span", { textContent: project.name }),
                    el("small", { textContent: `${project.numberOfSlides} slides` + (project.showInGrid ? "" : " · hidden") }));
                item.addEventListener("click", () => openEditor(project));
                return item;
            }));
    });
    $("#project-list").replaceChildren(...groups);
}

function openEditor(project) {
    if (formDirty && !confirm("Discard your unsaved changes to this project?")) return;
    selected = project ? project.name : null;
    formDirty = false;
    renderProjectList();

    const fragment = $("#editor-template").content.cloneNode(true);
    const form = fragment.querySelector("form");
    const fields = form.elements;
    const values = project ?? { name: "", category: state.categories[0].id, showInGrid: true, showOnHomepage: false };

    form.querySelector(".title").textContent = project ? project.name : "New project";
    fields.category.append(...state.categories.map(c => new Option(`${c.title} (${c.id})`, c.id)));
    fields.name.value = values.name;
    fields.category.value = values.category;
    fields.location.value = values.location ?? "";
    fields.description.value = values.description ?? "";
    fields.publications.value = values.publications ?? "";
    fields.showInGrid.checked = values.showInGrid;
    fields.showOnHomepage.checked = values.showOnHomepage;

    form.addEventListener("input", () => formDirty = true);
    form.addEventListener("submit", event => {
        event.preventDefault();
        run(() => saveProject(project, form), event.submitter);
    });

    const deleteButton = form.querySelector(".delete");
    const images = fragment.querySelector(".images");
    if (project) {
        deleteButton.addEventListener("click", () => run(() => deleteProject(project), deleteButton));
        images.querySelector(".cover-input").addEventListener("change", event => run(() => uploadCover(project.name, event.target)));
        images.querySelector(".slides-input").addEventListener("change", event => run(() => uploadSlides(project.name, event.target)));
    } else {
        deleteButton.remove();
        images.replaceChildren(el("p", { className: "muted", textContent: "Save the project first, then upload its images." }));
    }

    $("#editor").replaceChildren(fragment);
    if (project) renderImages(project);
    else fields.name.focus();
}

async function saveProject(project, form) {
    const fields = form.elements;
    const input = {
        name: fields.name.value.trim(),
        category: fields.category.value,
        location: fields.location.value,
        description: fields.description.value,
        publications: fields.publications.value,
        showInGrid: fields.showInGrid.checked,
        showOnHomepage: fields.showOnHomepage.checked,
    };
    const saved = project
        ? await api("PUT", `/projects/${encodeURIComponent(project.name)}`, input)
        : await api("POST", "/projects", input);

    formDirty = false;
    await reload();
    openEditor(projectByName(saved.name));
    toast("Saved.");
}

async function deleteProject(project) {
    if (!confirm(`Delete "${project.name}" and all of its images? This can't be undone.`)) return;

    await api("DELETE", `/projects/${encodeURIComponent(project.name)}`);
    formDirty = false;
    selected = undefined;
    await reload();
    $("#editor").replaceChildren(el("p", { className: "muted", textContent: `"${project.name}" was deleted.` }));
}

function imageUrl(project, file) {
    return `/projects/images/${encodeURIComponent(project.category)}/${encodeURIComponent(project.name)}/${file}?v=${imageStamp}`;
}

function renderImages(project) {
    const card = $("#editor .images");
    card.querySelector(".slide-count").textContent = project.numberOfSlides;
    card.querySelector(".cover").replaceChildren(project.hasCover
        ? el("img", { src: imageUrl(project, "main.webp"), alt: "Cover image" })
        : el("div", { className: "placeholder", textContent: "No cover image yet" }));

    card.querySelector(".slides").replaceChildren(...Array.from({ length: project.numberOfSlides }, (_, i) => {
        const index = i + 1;
        const url = imageUrl(project, `${index}.webp`);
        const remove = iconButton("×", `Delete slide ${index}`, false, () => run(() => deleteSlide(project, index), remove));
        remove.classList.add("remove");
        return el("figure", {},
            el("a", { href: url, target: "_blank" }, el("img", { src: url, loading: "lazy", alt: `Slide ${index}` })),
            el("figcaption", { textContent: index }),
            remove);
    }));
}

function updateProject(summary) {
    state.projects = state.projects.map(p => p.name === summary.name ? summary : p);
    renderProjectList();
    if (selected === summary.name) renderImages(summary);
}

function uploadForm(file) {
    const form = new FormData();
    form.append("file", file);
    return form;
}

async function uploadCover(name, input) {
    const file = input.files[0];
    input.value = "";
    if (!file) return;

    const progress = $("#editor .progress");
    progress.textContent = `Uploading ${file.name}…`;
    try {
        const summary = await api("PUT", `/projects/${encodeURIComponent(name)}/cover`, uploadForm(file));
        imageStamp = Date.now();
        updateProject(summary);
        toast("Cover image replaced.");
    } finally {
        progress.textContent = "";
    }
}

async function uploadSlides(name, input) {
    // Upload in file name order (1.jpg, 2.jpg, ..., 10.jpg), one request per file to keep requests small.
    const files = [...input.files].sort((a, b) => a.name.localeCompare(b.name, undefined, { numeric: true }));
    input.value = "";

    const progress = $("#editor .progress");
    let failed = 0;
    for (const [i, file] of files.entries()) {
        progress.textContent = `Uploading ${i + 1} of ${files.length}: ${file.name}…`;
        try {
            updateProject(await api("POST", `/projects/${encodeURIComponent(name)}/slides`, uploadForm(file)));
        } catch (error) {
            failed++;
            toast(`${file.name}: ${error.message}`, true);
        }
    }
    progress.textContent = "";
    if (files.length && !failed) toast(files.length === 1 ? "Slide added." : `${files.length} slides added.`);
}

async function deleteSlide(project, index) {
    if (!confirm(`Delete slide ${index}? The slides after it move up by one.`)) return;

    const summary = await api("DELETE", `/projects/${encodeURIComponent(project.name)}/slides/${index}`);
    imageStamp = Date.now();
    updateProject(summary);
}

/* Homepage & grid and promotional messages tabs */

function editDraft(key, change) {
    change(drafts[key] ??= [...state[key]]);
    if (key === "promotionalMessages") renderMessages();
    else renderList(key);
}

function swap(list, a, b) {
    [list[a], list[b]] = [list[b], list[a]];
}

function updateSaveButtons() {
    for (const key of Object.keys(listUrls))
        document.querySelector(`[data-list="${key}"] .save`).disabled = !(key in drafts);
}

function renderList(key) {
    const card = document.querySelector(`[data-list="${key}"]`);
    const items = drafts[key] ?? state[key];

    card.querySelector("ol").replaceChildren(...items.map((name, index) => el("li", {},
        el("span", { className: "name", textContent: name }),
        el("small", { className: "muted", textContent: categoryTitle(projectByName(name)?.category) }),
        iconButton("↑", "Move up", index === 0, () => editDraft(key, list => swap(list, index, index - 1))),
        iconButton("↓", "Move down", index === items.length - 1, () => editDraft(key, list => swap(list, index, index + 1))),
        iconButton("×", "Remove", false, () => editDraft(key, list => list.splice(index, 1))))));

    card.querySelector("select").replaceChildren(
        new Option("+ Add a project…", ""),
        ...state.projects.filter(p => !items.includes(p.name)).map(p => new Option(p.name, p.name)));
    updateSaveButtons();
}

function renderMessages() {
    const items = drafts.promotionalMessages ?? state.promotionalMessages;
    $("#messages").replaceChildren(...items.map((text, index) => {
        const textarea = el("textarea", { value: text, rows: 3 });
        textarea.addEventListener("input", () => {
            (drafts.promotionalMessages ??= [...state.promotionalMessages])[index] = textarea.value;
            updateSaveButtons();
        });
        return el("div", { className: "message" }, textarea,
            iconButton("×", "Remove", false, () => editDraft("promotionalMessages", list => list.splice(index, 1))));
    }));
    updateSaveButtons();
}

async function saveList(key) {
    state[key] = await api("PUT", listUrls[key], drafts[key]);
    delete drafts[key];
    // The projects' "show in ..." flags follow the lists.
    await reload();
    toast("Saved.");
}

/* Wiring */

$("#login-form").addEventListener("submit", async event => {
    event.preventDefault();
    const response = await fetch("/api/admin/login", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ password: $("#password").value }),
    });
    if (!response.ok) {
        $("#login-error").textContent = response.status === 429 ? "Too many attempts, wait a minute." : "Wrong password.";
        return;
    }
    $("#password").value = "";
    $("#login-error").textContent = "";
    await start();
});

$("#logout").addEventListener("click", async () => {
    await fetch("/api/admin/logout", { method: "POST" });
    location.reload();
});

for (const tab of document.querySelectorAll("[data-tab]")) {
    tab.addEventListener("click", () => {
        for (const other of document.querySelectorAll("[data-tab]")) other.classList.toggle("active", other === tab);
        for (const section of document.querySelectorAll(".tab")) section.hidden = section.id !== "tab-" + tab.dataset.tab;
    });
}

$("#new-project").addEventListener("click", () => openEditor(null));
$("#project-search").addEventListener("input", renderProjectList);

for (const key of ["homepageProjects", "projectsGrid"]) {
    const select = document.querySelector(`[data-list="${key}"] select`);
    select.addEventListener("change", () => select.value && editDraft(key, list => list.push(select.value)));
}
for (const key of Object.keys(listUrls)) {
    const save = document.querySelector(`[data-list="${key}"] .save`);
    save.addEventListener("click", () => run(() => saveList(key), save));
}
$("#add-message").addEventListener("click", () => {
    editDraft("promotionalMessages", list => list.push(""));
    $("#messages .message:last-child textarea").focus();
});

window.addEventListener("beforeunload", event => {
    if (formDirty || Object.keys(drafts).length) event.preventDefault();
});

start();
