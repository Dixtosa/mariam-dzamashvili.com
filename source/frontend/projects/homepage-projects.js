var homepageProjects =
	[
		"Park Home ვაკე",
		"ბინა იქსორი",
		"ბინა არტ ჰოლთან",
		"ბინა 'ბახტრიონი'",
		"ბინა კიკვიძის პარკთან"
	];

window.projectsGrid =
	[
		"Park Home ვაკე",
		"bina vashlijvari",
		"welcome to the jungle",
		"არქი პოლიტკოვსკაია",
		"ბინა black mood",
		"ბინა vintage house",
		"ბინა არტ ჰოლთან",
		"ბინა 'ბახტრიონი'",
		"ბინა ბეტონის ელემენტებით",
		"ბინა გაზაფხული",
		"ბინა გორგასალი",
		"ბინა იქსორი",
		"ბინა კიკვიძის პარკთან",
		"ბინა მინდელზე programmers house",
		"ბინა ნუცუბიძე",
		"ბინა ჟვანია",
		"ბინა ყიფშიძეზე flora",
		"პანორამა გაგარინი",
		"guest house in Nigeria",
		"ლისი house",
		"ლისი ტოპოგრაფი",
		"სახლი დიღომში",
		"სახლი თხინვალაში",
		"სახლი ივერთუბანში",
		"სახლი საგურამოში",
		"good choice",
		"sisauri grinders",
		"ნოტარიუსთა პალატა",
		"რესტორანი რაჭა",
		"საადვოკატო ბიურო Mirabeau devant Dreux-Brézé",
		"სალონი ritual",
		"საქართველოს გენერალური პროკურატურის დიზაინი",
		"საქართველოს ექსპერტიზის ეროვნული ბიურო",
		"სტომატოლოგიური კლინიკა ვაკე",
		"ტანსაცმლის ბუტიკი"
	];


let template = document.getElementById("homepage-project-template");
var homeHero = document.getElementById("home-hero");

for (let index = 0; index < homepageProjects.length; index++) {
	const element = homepageProjects[index];
	var newContent = template.cloneNode(true);
	newContent.innerHTML = Mustache.render(newContent.innerHTML, window.projectData[element]);
	homeHero.appendChild(newContent.content);
}