// homepageProjects, projectsGrid and projectData are loaded from the backend (api/data.js)

let template = document.getElementById("homepage-project-template");
var homeHero = document.getElementById("home-hero");

for (let index = 0; index < homepageProjects.length; index++) {
	const element = homepageProjects[index];
	var newContent = template.cloneNode(true);
	newContent.innerHTML = Mustache.render(newContent.innerHTML, window.projectData[element]);
	homeHero.appendChild(newContent.content);
}