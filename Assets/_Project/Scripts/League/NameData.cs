using System;
using System.Collections.Generic;

namespace PixFootball.League
{
    /// <summary>
    /// Raw name pools. All entries are generic given names / surnames; combinations are random and fictional.
    /// </summary>
    internal static class NameData
    {
        internal const string FirstRaw =
            "James John Robert Michael William David Richard Joseph Thomas Charles Christopher Daniel Matthew Anthony Mark Donald Steven Paul Andrew Joshua " +
            "Kenneth Kevin Brian George Timothy Ronald Edward Jason Jeffrey Ryan Jacob Gary Nicholas Eric Jonathan Stephen Larry Justin Scott Brandon " +
            "Benjamin Samuel Gregory Alexander Frank Patrick Raymond Jack Dennis Jerry Tyler Aaron Jose Adam Nathan Henry Douglas Zachary Peter Kyle " +
            "Walter Ethan Jeremy Harold Keith Christian Roger Noah Gerald Carl Terry Sean Austin Arthur Lawrence Jesse Dylan Bryan Joe Jordan Billy " +
            "Bruce Albert Willie Gabriel Logan Alan Juan Wayne Roy Ralph Randy Eugene Vincent Russell Elijah Louis Bobby Philip Johnny Bradley Isaiah " +
            "Marcus Darius Terrell Jamal Malik Deshawn Tavion Jaylen Demetrius Andre Cedric Dante Darnell Lamar Rashad Tyrone Xavier Zion Amari Kendrick " +
            "Devonte Javon Trevon Corey Cornell Dominic Isaac Mateo Santiago Diego Luis Carlos Miguel Javier Fernando Ricardo Alejandro Rafael Eduardo " +
            "Emilio Hector Ruben Armando Gustavo Marco Lorenzo Salvador Cristian Andres Sergio Mauricio Esteban Ignacio Rodrigo Joaquin Leonardo Adrian " +
            "Tomas Pablo Julio Cesar Enrique Ramon Arturo Gael Matias Bruno Santino Lucas Mason Liam Owen Caleb Hunter Connor Landon Jaxon Easton " +
            "Colton Brayden Cooper Carter Wyatt Grayson Declan Preston Brody Bryce Colby Chase Cody Garrett Trevor Spencer Blake Tanner Dalton Hayden " +
            "Jared Derek Travis Shane Cameron Dustin Clayton Marshall Weston Rhett Reid Beckett Ryder Tucker Lane Maddox Knox Jett Gunnar Axel Remington " +
            "Sawyer Kellen Brett Dillon Jace Kade Tristan Roman Silas Emmett Everett Anderson Brooks Cole Dawson Elliot Finn Graham Harrison Holden " +
            "Jasper Kayden Leland Milo Nolan Orion Quincy Rowan Sterling Thaddeus Ulysses Vance Warren Zane Abel Amos Barrett Bennett Clark Dean " +
            "Desmond Dexter Dwight Earl Elmer Emerson Floyd Franklin Gideon Glenn Grant Gus Hank Ivan Jonas Julian Kirk Lance Leon Lester Lloyd Mack " +
            "Morgan Neil Norman Oscar Otis Percy Reggie Rex Rudy Seth Stanley Stuart Theo Tobias Wade Walker Wesley Winston Alonzo Booker Calvin " +
            "Chester Clarence Clifton Cornelius Curtis Darrell Dewey Duane Elias Elton Emanuel Ernest Felix Fletcher Garland Hubert Irvin Jamar Jerome " +
            "Julius Kareem Kelvin Lamont Leroy Maurice Melvin Nelson Orlando Prince Quentin Rodney Roosevelt Sidney Terrance Titus Vernon Wendell Zeke " +
            "Kofi Kwame Chidi Emeka Tunde Obinna Sekou Mamadou Idris Yusuf Omar Hassan Karim Tariq Jabari Amir Jamil Rashid Nasir Samir Malachi " +
            "Joaquim Dmitri Nikolai Viktor Anton Stefan Tomasz Marek Lukas Jakub Matej Andrei Boris Leo Maxim Ivo Radek Milan Goran Luka Ante " +
            "Sione Tevita Mosese Siaosi Viliami Manu Isaako Fetu Lomano Tomasi Junior Aleki Taulupe Faafiu Penitani Kekoa Makoa Kai Keanu Koa Nalu " +
            "Haruto Kenji Hiro Takeshi Akira Daichi Ren Sora Jin Wei Chen Li Minh Tuan Hung Anh Khoa Vikram Arjun Rohan Dev Rahul Nikhil Sanjay " +
            "Callum Fraser Angus Hamish Ewan Murray Rory Duncan Cormac Declan Eamon Liam Niall Brendan Colm Fergus Kieran Ronan Seamus Tadhg Cillian " +
            "Giovanni Matteo Alessandro Luca Dario Enzo Fabio Gianluca Massimo Nico Paolo Raffaele Sandro Stefano Tito Valerio Antonio Carmine Dominick " +
            "Hans Otto Klaus Dieter Jurgen Lars Magnus Soren Anders Bjorn Erik Gustav Henrik Johan Nils Olaf Sven Torsten Ulrich Wolfgang Axl " +
            "Dashiell Ellis Hollis Jules Knute Lyle Merritt Nash Pierce Quinn Rafferty Shep Tate Vaughn Wilder Zander Ambrose Ansel Bram Cassius " +
            "Cyrus Dorian Evander Fitz Gannon Hale Ira Jarrett Kelby Lachlan Mercer Nixon Oakley Pryor Quill Rocco Slade Teague Upton Van " +
            "Wells Yancy Zeus Ashton Bishop Chandler Denton Eldon Foster Griffin Hudson Irving Jameson Keaton Landry Marlon Nestor Orville Pruitt " +
            "Ramsey Stetson Talon Urban Vernell Waylon Xander Yosef Zavier Alvin Barry Boyd Carlton Dell Ezra Gale Hobart Ike Jules Kermit " +
            "Lonnie Mitchell Nolen Odell Pete Rufus Sherman Tommie Virgil Weldon Abram Bastian Callan Dax Emory Fox Gage Harlan Isaias Jaiden " +
            "Kolton Lennox Mekhi Nico Osiris Paxton Rayan Saul Troy Uriel Valentin Westin Yahir Zayden Amos Brock Craig Drew Evan Floyd Glen " +
            "Hugh Ian Jay Kurt Lyman Miles Ned Owen Pierre Quincy Reed Sam Todd Uri Vic Will Xavi Yale Zach Alder Banks Cade Dane Eli Flynn";

        internal const string LastRaw =
            "Smith Johnson Williams Brown Jones Garcia Miller Davis Rodriguez Martinez Hernandez Lopez Gonzalez Wilson Anderson Thomas Taylor Moore Jackson Martin " +
            "Lee Perez Thompson White Harris Sanchez Clark Ramirez Lewis Robinson Walker Young Allen King Wright Scott Torres Nguyen Hill Flores Green " +
            "Adams Nelson Baker Hall Rivera Campbell Mitchell Carter Roberts Gomez Phillips Evans Turner Diaz Parker Cruz Edwards Collins Reyes Stewart Morris " +
            "Morales Murphy Cook Rogers Gutierrez Ortiz Morgan Cooper Peterson Bailey Reed Kelly Howard Ramos Kim Cox Ward Richardson Watson Brooks " +
            "Chavez Wood James Bennett Gray Mendoza Ruiz Hughes Price Alvarez Castillo Sanders Patel Myers Long Ross Foster Jimenez Powell Jenkins Perry " +
            "Russell Sullivan Bell Coleman Butler Henderson Barnes Gonzales Fisher Vasquez Simmons Romero Jordan Patterson Alexander Hamilton Graham Reynolds " +
            "Griffin Wallace Moreno West Cole Hayes Bryant Herrera Gibson Ellis Tran Medina Aguilar Stevens Murray Ford Castro Marshall Owens Harrison " +
            "Fernandez McDonald Woods Washington Kennedy Wells Vargas Henry Chen Freeman Webb Tucker Guzman Burns Crawford Olson Simpson Porter Hunter " +
            "Gordon Mendez Silva Shaw Snyder Mason Dixon Munoz Hunt Hicks Holmes Palmer Wagner Black Robertson Boyd Rose Stone Salazar Fox Warren Mills " +
            "Meyer Rice Schmidt Garza Daniels Ferguson Nichols Stephens Soto Weaver Ryan Gardner Payne Grant Dunn Kelley Spencer Hawkins Arnold Pierce " +
            "Vazquez Hansen Peters Santos Hart Bradley Knight Elliott Cunningham Duncan Armstrong Hudson Carroll Lane Riley Andrews Alvarado Ray Delgado " +
            "Berry Perkins Hoffman Johnston Matthews Pena Richards Contreras Willis Carpenter Lawrence Sandoval Guerrero George Chapman Rios Estrada Ortega " +
            "Watkins Greene Nunez Wheeler Valdez Harper Burke Larson Santiago Maldonado Morrison Franklin Carlson Austin Dominguez Carr Lawson Jacobs " +
            "Obrien Lynch Singh Vega Bishop Montgomery Oliver Jensen Harvey Williamson Gilbert Dean Sims Espinoza Howell Li Wong Reid Hanson Le " +
            "McCoy Garrett Burton Fuller Wang Weber Welch Rojas Lucas Marquez Fields Park Yang Little Banks Padilla Day Walsh Bowman Schultz Luna " +
            "Fowler Mejia Davidson Acosta Brewer May Holland Juarez Newman Pearson Curtis Cortez Douglas Schneider Joseph Barrett Navarro Figueroa Keller " +
            "Avila Wade Molina Stanley Hopkins Campos Barnett Bates Chambers Caldwell Beck Lambert Miranda Byrd Craig Ayala Lowe Frazier Powers Neal " +
            "Leonard Gregory Carrillo Sutton Fleming Rhodes Shelton Schwartz Norris Jennings Watts Duran Walters Cohen McDaniel Moran Parks Steele " +
            "Vaughn Becker Holt Deleon Barker Terry Hale Leon Hail Benson Haynes Horton Miles Lyons Pham Graves Bush Thornton Wolfe Warner Cabrera " +
            "McKinney Mann Zimmerman Dawson Lara Fletcher Page McCarthy Love Robles Cervantes Solis Erickson Reeves Chang Klein Salinas Fuentes " +
            "Baldwin Daniel Simon Velasquez Hardy Higgins Aguirre Lin Cummings Chandler Sharp Barber Bowen Ochoa Dennis Robbins Liu Ramsey Francis " +
            "Griffith Paul Blair Oconnor Cardenas Pacheco Cross Calderon Quinn Moss Swanson Chan Rivas Khan Rodgers Serrano Fitzgerald Rosales Stevenson " +
            "Christensen Manning Gill Curry McLaughlin Harmon McGee Gross Doyle Garner Newton Burgess Reilly Goodwin Mack Cline Mooney Hurst Rowe " +
            "Hardin Kane Lowery Mcbride Pittman Sawyer Whitaker Yates Zuniga Abbott Acevedo Albright Ashby Atkins Avery Bauer Bender Blevins Blackburn " +
            "Boone Bowers Boyle Bridges Briggs Buckley Burch Burnett Cain Calhoun Cannon Carey Carver Cash Chase Clay Cobb Combs Conley Conrad " +
            "Cooke Cotton Coy Cranford Crowley Dalton Davenport Decker Dillard Dillon Dodson Donovan Dorsey Drake Dudley Duffy Dyer Eaton Emerson " +
            "Engel Everett Farley Farmer Finley Fischer Flynn Foley Forbes Franks Frost Fry Gaines Gallagher Gamble Gentry Gibbs Giles Glover " +
            "Goff Gould Grimes Guthrie Hagen Hammond Hancock Hardesty Harding Hartman Hatfield Hayden Heath Hendrix Hess Hobbs Hodge Hogan Holloway " +
            "Hooper Hubbard Hull Hurley Ingram Irwin Jarvis Jeter Joyner Kaiser Kemp Kerr Kidd Kirby Kirk Knapp Kramer Lang Leach Leblanc " +
            "Lindsey Lockhart Logan Lopes Lovett Lowry Lundy Lutz Macias Madden Mahoney Malone Marks Mathis Maxwell Mayo McBride McCall McClain " +
            "McCormick McCullough McDowell McFarland McGuire McIntosh McKay McKenzie McLean McMahon McNeil Meadows Merritt Mercer Metcalf Mckee Mize " +
            "Monroe Moody Moyer Nash Nielsen Nixon Noble Nolan Norton Nye Odom Oliveira Orr Osborn Otero Owen Pace Pagan Parrish Pate Patton " +
            "Pickett Pierson Pike Pittman Poole Pope Prater Preston Pruitt Purcell Rader Rankin Rasmussen Ratliff Rawls Rector Redd Reese Rhoades " +
            "Rich Rigsby Riggs Roach Roark Robbins Roman Rowland Roy Rush Rutledge Salas Samuels Sargent Saunders Savage Schaefer Schroeder Seals " +
            "Sexton Shannon Shepherd Sheppard Sherman Shields Shipley Sizemore Skinner Slater Sloan Snow Sparks Spears Stafford Stein Stokes Strong " +
            "Strickland Stuart Sweeney Tanner Tate Tatum Teague Tillman Timmons Todd Tolbert Townsend Travis Trujillo Tyler Underwood Upton Valencia " +
            "Vance Vang Vaughan Villa Vincent Wagoner Waller Walton Ware Watters Weeks Whitley Wiggins Wilder Wiley Winters Wolf Woodard Woodruff " +
            "Wooten Wyatt Yoder Zamora Abernathy Alder Ames Ballard Barlow Beal Beaumont Bellamy Birch Blackwell Bloom Bolton Boswell Brandt Brennan " +
            "Brock Bryce Burkett Cantrell Castellano Chadwick Clemons Colby Colson Crane Crockett Dahl Dantzler Delaney Denham Devlin Dobbs Dugan " +
            "Eastman Eller Ellsworth Falk Farris Faulkner Fenton Fitch Fontaine Forsyth Gaffney Galvin Garrison Gatlin Gilmore Goddard Gorman Gunn " +
            "Haas Hadley Halloran Hanley Harlow Hartley Hastings Hawthorne Hendricks Hightower Holcomb Hollis Huber Ingle Iverson Jaeger Jernigan " +
            "Kessler Kincaid Kinney Kowalski Lacey Landry Larkin Lassiter Latham Lennox Lombardi Lorenz Lovelace Mabry Magee Maguire Mallory Mancini " +
            "Marlowe Mattingly Maynard Mcallister Mcgrath Medley Merrick Mohr Moser Mullins Nagel Novak Nunn Oakes Odell Okafor Okoye Olsen Ortega " +
            "Osei Oyelaran Pagano Pelletier Penn Perrin Petrov Pruett Quigley Radcliffe Ramsay Rhymes Rinaldi Rosario Rossi Rourke Sabo Salerno " +
            "Santoro Sato Savoy Seymour Shapiro Sinclair Sorensen Sparrow Stamper Steward Storm Sutter Talbot Thorne Tolliver Toomey Trent Tripp " +
            "Turnbull Ulrich Urbina Vanderbilt Varga Venable Vidal Villanueva Voss Wainwright Walden Wardell Wexler Whitfield Wickham Wiseman Woolsey " +
            "Yarbrough Zeller Zielinski Adeyemi Afolabi Agyeman Akintola Amadi Asante Boateng Chukwu Diallo Eze Mensah Nwosu Obi Okonkwo Osagie " +
            "Tafuna Fonoti Leiataua Faumuina Mataafa Taliaoa Tuiasosopo Lauvao Seumalo Vaeao Alapati Kahale Kealoha Nakamura Tanaka Watanabe Yamamoto Kobayashi " +
            "Takahashi Ito Shimizu Hayashi Saito Mori Ikeda Sung Choi Jeong Yoon Han Tran Nguyen Pham Vu Lam Dang Truong Bui Do Ngo Duong " +
            "Gupta Sharma Mehta Kapoor Reddy Iyer Nair Banerjee Chopra Desai Haddad Khalil Nasser Mansour Saleh Aziz Farah Rahman Chowdhury Hossain " +
            "Kovacs Horvat Novotny Dvorak Nowak Wisniewski Kaminski Lewandowski Zielinska Popescu Ionescu Dumitru Stoica Petrovic Jovanovic Markovic Kostic " +
            "Murphy Brennan Doherty Gallagher Kavanagh Flanagan Donnelly Gormley Hegarty McCabe Nolan Sheehan Whelan Duffy Lynch Fitzpatrick Costello Cullen " +
            "Ferrari Esposito Bianchi Romano Colombo Ricci Marino Greco Bruno Gallo Conti DeLuca Mancuso Costa Giordano Rizzo Lombardo Moretti Barbieri " +
            "Schneider Fischer Weber Wagner Becker Hoffmann Koch Richter Klein Wolf Neumann Schwarz Zimmermann Braun Kruger Hofmann Hartmann Lange Werner " +
            "Lindgren Johansson Andersson Karlsson Nilsson Eriksson Larsson Olsson Persson Svensson Gustafsson Pettersson Jonsson Jansson Hansen Pedersen Nielsen " +
            "Aldridge Ashworth Bancroft Barnhart Blackmon Bledsoe Bonner Brantley Burrell Calloway Coffey Dukes Eason Fairchild Gaither Goins Gresham " +
            "Hairston Heard Hinton Ivory Jeffries Kidder Lofton Lyle Mabon Mims Neely Ogden Pettis Quarles Redmond Rucker Satterfield Sledge Stallworth " +
            "Tolson Tyree Vickers Wimbush Wingo Yarborough Zeigler Crosby Fenner Gadsden Haywood Ledbetter Mayfield Pearce Prewitt Ravenel Spivey Tisdale";

        internal static readonly string[] ProCities =
        {
            "Hartford", "Richmond", "Norfolk", "Albany", "Milwaukee", "Columbus", "Louisville", "Rochester", "Charleston", "Birmingham",
            "Memphis", "Savannah", "Albuquerque", "Tucson", "El Paso", "Reno", "Providence", "Trenton", "Wilmington", "Manchester",
            "Omaha", "Des Moines", "Madison", "Wichita", "San Antonio", "Austin", "Oklahoma City", "Baton Rouge", "Portland", "Sacramento",
            "Spokane", "Honolulu", "Boise", "Tulsa", "Little Rock", "Knoxville", "Raleigh", "Greensboro", "Orlando", "Mobile",
            "Jackson", "Lexington", "Dayton", "Akron", "Toledo", "Fort Wayne", "Grand Rapids", "Duluth", "Fargo", "Sioux Falls",
            "Lincoln", "Topeka", "Cheyenne", "Billings", "Anchorage", "Fresno", "Bakersfield", "Santa Fe", "Amarillo", "Lubbock",
            "Shreveport", "Pensacola", "Chattanooga", "Harrisburg", "Scranton", "Syracuse", "Burlington", "Concord", "Portsmouth", "Newport"
        };

        internal static readonly string[] ProNicknames =
        {
            "Cannons", "Sentinels", "Breakers", "Stormcrows", "Foundrymen", "Pioneers", "Riverkings", "Lakehawks", "Corsairs", "Furnace",
            "Riverboats", "Marauders", "Sunhawks", "Copperheads", "Scorpions", "Silverbacks", "Lanterns", "Hammers", "Bluecoats", "Millers",
            "Stampeders", "Voyagers", "Northmen", "Aviators", "Missionaries", "Outlaws", "Wranglers", "Bayou Kings", "Ironclads", "Prospectors",
            "Lumberjacks", "Tradewinds", "Blizzard", "Gladiators", "Harpoons", "Mustangers", "Thunderheads", "Wildfire", "Vanguard", "Rampage",
            "Condors", "Cutthroats", "Dragoons", "Embers", "Frostbite", "Brownbacks", "Galeforce", "Juggernauts", "Kodiaks", "Plainshorns",
            "Maulers", "Nighthawks", "Overlords", "Pathfinders", "Quakes", "Talonbirds", "Cutlasses", "Behemoths", "Undertow", "Vipers",
            "Warhawks", "Rustlers", "Yellowjackets", "Zephyrs", "Armada", "Brawlers", "Clippers", "Drifters", "Engineers", "Falconers"
        };

        internal static readonly string[] Hometowns =
        {
            "Atlanta, GA", "Houston, TX", "Dallas, TX", "Miami, FL", "Tampa, FL", "Orlando, FL", "Jacksonville, FL", "New Orleans, LA", "Baton Rouge, LA", "Birmingham, AL",
            "Mobile, AL", "Montgomery, AL", "Jackson, MS", "Memphis, TN", "Nashville, TN", "Knoxville, TN", "Charlotte, NC", "Raleigh, NC", "Columbia, SC", "Charleston, SC",
            "Richmond, VA", "Norfolk, VA", "Baltimore, MD", "Philadelphia, PA", "Pittsburgh, PA", "Newark, NJ", "Brooklyn, NY", "Buffalo, NY", "Boston, MA", "Hartford, CT",
            "Cleveland, OH", "Columbus, OH", "Cincinnati, OH", "Detroit, MI", "Chicago, IL", "Indianapolis, IN", "Milwaukee, WI", "Minneapolis, MN", "St. Louis, MO", "Kansas City, MO",
            "Omaha, NE", "Des Moines, IA", "Oklahoma City, OK", "Tulsa, OK", "Little Rock, AR", "San Antonio, TX", "Austin, TX", "El Paso, TX", "Denver, CO", "Salt Lake City, UT",
            "Phoenix, AZ", "Las Vegas, NV", "Los Angeles, CA", "San Diego, CA", "Oakland, CA", "Sacramento, CA", "Fresno, CA", "Portland, OR", "Seattle, WA", "Honolulu, HI",
            "Compton, CA", "Long Beach, CA", "Inglewood, CA", "Lakeland, FL", "Pahokee, FL", "Belle Glade, FL", "Valdosta, GA", "Macon, GA", "Savannah, GA", "Augusta, GA",
            "Shreveport, LA", "Lafayette, LA", "Beaumont, TX", "Galveston, TX", "Lubbock, TX", "Amarillo, TX", "Midland, TX", "Odessa, TX", "Tyler, TX", "Waco, TX",
            "Toledo, OH", "Akron, OH", "Dayton, OH", "Youngstown, OH", "Gary, IN", "Fort Wayne, IN", "Peoria, IL", "Rockford, IL", "Madison, WI", "Green Bay, WI",
            "Boise, ID", "Spokane, WA", "Tacoma, WA", "Anchorage, AK", "Albuquerque, NM", "Santa Fe, NM", "Wichita, KS", "Topeka, KS", "Sioux Falls, SD", "Fargo, ND"
        };

        // ---- invented college place names ----
        internal static readonly string[] PlaceStart =
        {
            "Bar", "Bel", "Bram", "Cal", "Car", "Cas", "Cor", "Dal", "Den", "Dun", "Eld", "Fal", "Fen", "Gal", "Gar", "Glen", "Hal", "Har", "Hol", "Jar",
            "Kel", "Kin", "Lan", "Lor", "Mar", "Mel", "Mor", "Nor", "Orm", "Pel", "Quin", "Ral", "Red", "Ros", "Sal", "Sel", "Sher", "Tal", "Tar", "Thor",
            "Ul", "Val", "Ven", "Wal", "Wen", "Win", "Yar", "Zel", "Ash", "Brad", "Cam", "Dev", "Ever", "Flor", "Grey", "Hawk", "Iron", "Jun", "Kest", "Lex"
        };

        internal static readonly string[] PlaceMiddle =
        {
            "", "", "", "a", "e", "i", "o", "en", "er", "an", "or", "al", "il", "on", "ar"
        };

        internal static readonly string[] PlaceEnd =
        {
            "ton", "ville", "ford", "field", "burg", "wood", "dale", "mont", "haven", "port", "view", "ridge", "land", "stead", "wick", "bury",
            "cliff", "crest", "worth", "mouth", "brook", "gate", "holm", "mere", "shire", "vale", "grove", "bridge", "hurst", "ley", "ham", "lake"
        };

        internal static readonly string[] GeoWords =
        {
            "Cedar", "Ridge", "Bay", "Prairie", "Summit", "Harbor", "Canyon", "Mesa", "Delta", "Pine", "Granite", "Timber", "Lakeshore", "Bluff", "Falls",
            "Meadow", "Hollow", "Cove", "Basin", "Valley", "Highland", "Lowland", "Shoals", "Pointe", "Orchard", "Redwood", "Sandstone", "Cypress", "Birch", "Willow"
        };

        internal static readonly string[] Directions = { "Northern", "Southern", "Eastern", "Western", "Central", "Coastal", "Upper", "Lower" };

        internal static readonly string[] SchoolMascots =
        {
            "Wolverhounds", "Gryphons", "Stags", "Lynx", "Sandpipers", "Mountaineers", "Pioneers", "Highlanders", "Cavaliers", "Rangers",
            "Bobcats", "Herons", "Peregrines", "Otters", "Badgers", "Bison", "Cougars", "Dragons", "Windhawks", "Foxes",
            "Griffins", "Hornets", "Ibis", "Jackals", "Kestrels", "Lancers", "Mustangs", "Nomads", "Owls", "Prowlcats",
            "Quakers", "Cragrams", "Hoplites", "Tigers", "Unicorns", "Longshipmen", "Wildcats", "Yetis", "Zephyrs", "Trailmen",
            "Brigands", "Captains", "Rebels", "Crusaders", "Colossals", "Knights", "Skyraiders", "Bolters", "Bulldogs", "Hawks",
            "Ospreys", "Pelicans", "Stallions", "Thunder", "Blaze", "Comets", "Voyagers", "Miners", "Foresters", "Anglers",
            "Coyotes", "Armadillos", "Salamanders", "Gators", "Terriers", "Wolves", "Rattlers", "Redbirds", "Skyjays", "Blue Herons",
            "Harriers", "Ironmen", "Mariners", "Sentries", "Scouts", "Pilots", "Barons", "Dukes", "Monarchs", "Regents"
        };

        internal static readonly string[] BowlNames =
        {
            "Sunrise Bowl", "Harvest Bowl", "Lantern Bowl", "Rosewater Bowl", "Orchard Bowl", "Summit Bowl", "Capital Bowl", "Gulfline Bowl",
            "Pinecone Bowl", "Redstone Bowl", "Skyline Bowl", "Riverwalk Bowl", "Frostfire Bowl", "Mesa Verde Bowl", "Lakeshore Classic", "Ironworks Bowl",
            "Cornerstone Bowl", "Heritage Bowl", "Beacon Bowl", "Driftwood Bowl", "Silverleaf Bowl", "Goldenrod Bowl", "Canyon Classic", "Founders Bowl",
            "Magnolia Bowl", "Tidewater Bowl", "Sandhill Bowl", "Evergreen Bowl", "Copperline Bowl", "Prairie Fire Bowl", "Bluegrass Bowl", "Fanpalm Bowl",
            "Lighthouse Bowl", "Foothills Bowl", "Crossroads Bowl", "Starlight Bowl", "Wildcat Run Bowl", "Pioneer Trail Bowl", "Northern Lights Bowl", "Amber State Classic"
        };

        /// <summary>
        /// Real players / coaches whose "First Last" can fall out of the generic given-name and surname pools (QA-583). The generator rejects these
        /// combinations. Hand-written, lower case; not exhaustive, but covers every name the audit found plus the best-known stars.
        /// </summary>
        internal static readonly string[] RealNameBlocklist =
        {
            "jerry rice", "barry sanders", "reggie white", "randy moss", "lawrence taylor", "terrell owens", "aaron rodgers", "lamar jackson", "jared goff", "jordan love",
            "trevor lawrence", "russell wilson", "calvin johnson", "patrick willis", "john madden", "walter payton", "emmitt smith", "jim brown", "joe montana", "dan marino",
            "john elway", "brett favre", "drew brees", "patrick mahomes", "josh allen", "joe burrow", "justin herbert", "dak prescott", "derrick henry", "saquon barkley",
            "christian mccaffrey", "travis kelce", "tyreek hill", "justin jefferson", "davante adams", "cooper kupp", "stefon diggs", "julio jones", "antonio brown",
            "odell beckham", "adrian peterson", "ladainian tomlinson", "marshall faulk", "ray lewis", "brian urlacher", "ed reed", "troy polamalu", "deion sanders",
            "darrelle revis", "richard sherman", "aaron donald", "von miller", "khalil mack", "myles garrett", "nick bosa", "micah parsons", "kyler murray", "jalen hurts",
            "matthew stafford", "joe flacco", "ben roethlisberger", "eli manning", "peyton manning", "philip rivers", "cam newton", "andrew luck", "carson palmer",
            "michael vick", "steve young", "troy aikman", "kurt warner", "johnny unitas", "bart starr", "roger staubach", "terry bradshaw", "joe namath", "dick butkus",
            "mike singletary", "jack lambert", "joe greene", "bruce smith", "michael strahan", "warren sapp", "derrick thomas", "junior seau", "anthony munoz",
            "larry fitzgerald", "hines ward", "torry holt", "isaac bruce", "cris carter", "tim brown", "michael irvin", "marvin harrison", "andre johnson", "steve smith",
            "roddy white", "wes welker", "tony gonzalez", "antonio gates", "jason witten", "shannon sharpe", "brian dawkins", "charles woodson", "champ bailey",
            "rod woodson", "ronnie lott", "darrell green", "joe thomas", "jonathan ogden", "orlando pace", "walter jones", "tony dungy", "andy reid", "mike tomlin",
            "sean payton", "bill parcells", "vince lombardi", "don shula", "chuck noll", "joe gibbs", "bill walsh", "tom landry", "jimmy johnson", "pete carroll",
            "john harbaugh", "mike mccarthy", "sean mcvay", "kyle shanahan", "dan reeves", "marv levy", "bruce arians", "mike shanahan", "bill cowher", "jon gruden",
            "nick saban", "urban meyer", "terrell davis", "ricky williams", "steve largent", "eric dickerson", "earl campbell", "marcus allen", "curtis martin",
            "jamal lewis", "edgerrin james", "frank gore", "chris johnson", "arian foster", "ezekiel elliott", "dalvin cook", "alvin kamara", "nick chubb", "jonathan taylor",
            "austin ekeler", "leonard fournette", "mark ingram", "marshawn lynch", "tom brady", "rob gronkowski", "michael jordan", "james brown", "george washington",
            "tony romo", "alex smith", "jay cutler", "sam bradford", "mark sanchez", "ryan tannehill", "ryan fitzpatrick", "tua tagovailoa", "baker mayfield", "kirk cousins",
            "derek carr", "teddy bridgewater", "geno smith", "jameis winston", "mitchell trubisky", "jimmy garoppolo", "daniel jones", "bryce young", "caleb williams",
            "jayden daniels", "drake maye", "bo nix", "c.j. stroud", "anthony richardson", "justin fields", "zach wilson", "mac jones", "trey lance", "joe mixon",
            "devin white", "fred warner", "bobby wagner", "luke kuechly", "lavonte david", "tj watt", "jj watt", "tj hockenson", "george kittle", "mark andrews",
            "darren waller", "dallas goedert", "evan engram", "jared allen", "julius peppers", "demarcus ware", "jason taylor", "simeon rice", "chris jones", "cameron jordan",
            "joey bosa", "maxx crosby", "trent williams", "tyron smith", "zack martin", "joe staley", "jason kelce", "maurkice pouncey", "jalen ramsey", "patrick peterson",
            "stephon gilmore", "marlon humphrey", "tyrann mathieu", "earl thomas", "kam chancellor", "harrison smith", "minkah fitzpatrick", "derwin james", "jamal adams",
            "justin tucker", "adam vinatieri", "morten andersen", "sebastian janikowski", "matt prater", "pat mcafee", "shane lechler", "ray guy", "jeff wilson",
            "alvin harper", "keenan allen", "mike evans", "adam thielen", "amari cooper", "deandre hopkins", "dk metcalf", "a.j. brown", "ceedee lamb", "jaylen waddle",
            "jamarr chase", "garrett wilson", "chris olave", "drake london", "puka nacua", "amon-ra st. brown", "brandon aiyuk", "deebo samuel", "terry mclaurin"
        };
    }
}
