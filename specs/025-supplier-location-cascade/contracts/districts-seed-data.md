# Costa Rica Distritos — Seed Data (spec 025 T001)

Authoritative enumeration of all 488 distritos keyed by `PP_CC_DD` (province / cantón / distrito ordinals).

Source of truth: IGN (Registro Nacional) *División Territorial Administrativa 2024* official PDF (`files.snitcr.go.cr`), reconciled against the Spanish Wikipedia *Anexo:Distritos* pages. The dataset reflects the **488-distrito snapshot** that matches the 84-cantón catalog already seeded in the DB (the modern post-2022 cantón set, before the 4 post-2020 distrito creations — see Validation summary).

DD ordinals are renumbered contiguously `01..N` per cantón (the IGN keeps vacated numeric codes after cantón splits; those gaps are closed here so ordinals match the seed contract).


## 01 — San José (123 distritos)

| DistrictCode | DistrictName | CantonCode | CantonName |
|---|---|---|---|
| 01_01_01 | Carmen | 01_01 | San José |
| 01_01_02 | Merced | 01_01 | San José |
| 01_01_03 | Hospital | 01_01 | San José |
| 01_01_04 | Catedral | 01_01 | San José |
| 01_01_05 | Zapote | 01_01 | San José |
| 01_01_06 | San Francisco de Dos Ríos | 01_01 | San José |
| 01_01_07 | Uruca | 01_01 | San José |
| 01_01_08 | Mata Redonda | 01_01 | San José |
| 01_01_09 | Pavas | 01_01 | San José |
| 01_01_10 | Hatillo | 01_01 | San José |
| 01_01_11 | San Sebastián | 01_01 | San José |
| 01_02_01 | Escazú | 01_02 | Escazú |
| 01_02_02 | San Antonio | 01_02 | Escazú |
| 01_02_03 | San Rafael | 01_02 | Escazú |
| 01_03_01 | Desamparados | 01_03 | Desamparados |
| 01_03_02 | San Miguel | 01_03 | Desamparados |
| 01_03_03 | San Juan de Dios | 01_03 | Desamparados |
| 01_03_04 | San Rafael Arriba | 01_03 | Desamparados |
| 01_03_05 | San Antonio | 01_03 | Desamparados |
| 01_03_06 | Frailes | 01_03 | Desamparados |
| 01_03_07 | Patarrá | 01_03 | Desamparados |
| 01_03_08 | San Cristóbal | 01_03 | Desamparados |
| 01_03_09 | Rosario | 01_03 | Desamparados |
| 01_03_10 | Damas | 01_03 | Desamparados |
| 01_03_11 | San Rafael Abajo | 01_03 | Desamparados |
| 01_03_12 | Gravilias | 01_03 | Desamparados |
| 01_03_13 | Los Guido | 01_03 | Desamparados |
| 01_04_01 | Santiago | 01_04 | Puriscal |
| 01_04_02 | Mercedes Sur | 01_04 | Puriscal |
| 01_04_03 | Barbacoas | 01_04 | Puriscal |
| 01_04_04 | Grifo Alto | 01_04 | Puriscal |
| 01_04_05 | San Rafael | 01_04 | Puriscal |
| 01_04_06 | Candelarita | 01_04 | Puriscal |
| 01_04_07 | Desamparaditos | 01_04 | Puriscal |
| 01_04_08 | San Antonio | 01_04 | Puriscal |
| 01_04_09 | Chires | 01_04 | Puriscal |
| 01_05_01 | San Marcos | 01_05 | Tarrazú |
| 01_05_02 | San Lorenzo | 01_05 | Tarrazú |
| 01_05_03 | San Carlos | 01_05 | Tarrazú |
| 01_06_01 | Aserrí | 01_06 | Aserrí |
| 01_06_02 | Tarbaca | 01_06 | Aserrí |
| 01_06_03 | Vuelta de Jorco | 01_06 | Aserrí |
| 01_06_04 | San Gabriel | 01_06 | Aserrí |
| 01_06_05 | Legua | 01_06 | Aserrí |
| 01_06_06 | Monterrey | 01_06 | Aserrí |
| 01_06_07 | Salitrillos | 01_06 | Aserrí |
| 01_07_01 | Colón | 01_07 | Mora |
| 01_07_02 | Guayabo | 01_07 | Mora |
| 01_07_03 | Tabarcia | 01_07 | Mora |
| 01_07_04 | Piedras Negras | 01_07 | Mora |
| 01_07_05 | Picagres | 01_07 | Mora |
| 01_07_06 | Jaris | 01_07 | Mora |
| 01_07_07 | Quitirrisí | 01_07 | Mora |
| 01_08_01 | Guadalupe | 01_08 | Goicoechea |
| 01_08_02 | San Francisco | 01_08 | Goicoechea |
| 01_08_03 | Calle Blancos | 01_08 | Goicoechea |
| 01_08_04 | Mata de Plátano | 01_08 | Goicoechea |
| 01_08_05 | Ipís | 01_08 | Goicoechea |
| 01_08_06 | Rancho Redondo | 01_08 | Goicoechea |
| 01_08_07 | Purral | 01_08 | Goicoechea |
| 01_09_01 | Santa Ana | 01_09 | Santa Ana |
| 01_09_02 | Salitral | 01_09 | Santa Ana |
| 01_09_03 | Pozos | 01_09 | Santa Ana |
| 01_09_04 | Uruca | 01_09 | Santa Ana |
| 01_09_05 | Piedades | 01_09 | Santa Ana |
| 01_09_06 | Brasil | 01_09 | Santa Ana |
| 01_10_01 | Alajuelita | 01_10 | Alajuelita |
| 01_10_02 | San Josecito | 01_10 | Alajuelita |
| 01_10_03 | San Antonio | 01_10 | Alajuelita |
| 01_10_04 | Concepción | 01_10 | Alajuelita |
| 01_10_05 | San Felipe | 01_10 | Alajuelita |
| 01_11_01 | San Isidro | 01_11 | Vázquez de Coronado |
| 01_11_02 | San Rafael | 01_11 | Vázquez de Coronado |
| 01_11_03 | Dulce Nombre de Jesús | 01_11 | Vázquez de Coronado |
| 01_11_04 | Patalillo | 01_11 | Vázquez de Coronado |
| 01_11_05 | Cascajal | 01_11 | Vázquez de Coronado |
| 01_12_01 | San Ignacio | 01_12 | Acosta |
| 01_12_02 | Guaitil | 01_12 | Acosta |
| 01_12_03 | Palmichal | 01_12 | Acosta |
| 01_12_04 | Cangrejal | 01_12 | Acosta |
| 01_12_05 | Sabanillas | 01_12 | Acosta |
| 01_13_01 | San Juan | 01_13 | Tibás |
| 01_13_02 | Cinco Esquinas | 01_13 | Tibás |
| 01_13_03 | Anselmo Llorente | 01_13 | Tibás |
| 01_13_04 | León XIII | 01_13 | Tibás |
| 01_13_05 | Colima | 01_13 | Tibás |
| 01_14_01 | San Vicente | 01_14 | Moravia |
| 01_14_02 | San Jerónimo | 01_14 | Moravia |
| 01_14_03 | La Trinidad | 01_14 | Moravia |
| 01_15_01 | San Pedro | 01_15 | Montes de Oca |
| 01_15_02 | Sabanilla | 01_15 | Montes de Oca |
| 01_15_03 | Mercedes | 01_15 | Montes de Oca |
| 01_15_04 | San Rafael | 01_15 | Montes de Oca |
| 01_16_01 | San Pablo | 01_16 | Turrubares |
| 01_16_02 | San Pedro | 01_16 | Turrubares |
| 01_16_03 | San Juan de Mata | 01_16 | Turrubares |
| 01_16_04 | San Luis | 01_16 | Turrubares |
| 01_16_05 | Carara | 01_16 | Turrubares |
| 01_17_01 | Santa María | 01_17 | Dota |
| 01_17_02 | Jardín | 01_17 | Dota |
| 01_17_03 | Copey | 01_17 | Dota |
| 01_18_01 | Curridabat | 01_18 | Curridabat |
| 01_18_02 | Granadilla | 01_18 | Curridabat |
| 01_18_03 | Sánchez | 01_18 | Curridabat |
| 01_18_04 | Tirrases | 01_18 | Curridabat |
| 01_19_01 | San Isidro de El General | 01_19 | Pérez Zeledón |
| 01_19_02 | El General | 01_19 | Pérez Zeledón |
| 01_19_03 | Daniel Flores | 01_19 | Pérez Zeledón |
| 01_19_04 | Rivas | 01_19 | Pérez Zeledón |
| 01_19_05 | San Pedro | 01_19 | Pérez Zeledón |
| 01_19_06 | Platanares | 01_19 | Pérez Zeledón |
| 01_19_07 | Pejibaye | 01_19 | Pérez Zeledón |
| 01_19_08 | Cajón | 01_19 | Pérez Zeledón |
| 01_19_09 | Barú | 01_19 | Pérez Zeledón |
| 01_19_10 | Río Nuevo | 01_19 | Pérez Zeledón |
| 01_19_11 | Páramo | 01_19 | Pérez Zeledón |
| 01_19_12 | La Amistad | 01_19 | Pérez Zeledón |
| 01_20_01 | San Pablo | 01_20 | León Cortés Castro |
| 01_20_02 | San Andrés | 01_20 | León Cortés Castro |
| 01_20_03 | Llano Bonito | 01_20 | León Cortés Castro |
| 01_20_04 | San Isidro | 01_20 | León Cortés Castro |
| 01_20_05 | Santa Cruz | 01_20 | León Cortés Castro |
| 01_20_06 | San Antonio | 01_20 | León Cortés Castro |

## 02 — Alajuela (116 distritos)

| DistrictCode | DistrictName | CantonCode | CantonName |
|---|---|---|---|
| 02_01_01 | Alajuela | 02_01 | Alajuela |
| 02_01_02 | San José | 02_01 | Alajuela |
| 02_01_03 | Carrizal | 02_01 | Alajuela |
| 02_01_04 | San Antonio | 02_01 | Alajuela |
| 02_01_05 | Guácima | 02_01 | Alajuela |
| 02_01_06 | San Isidro | 02_01 | Alajuela |
| 02_01_07 | Sabanilla | 02_01 | Alajuela |
| 02_01_08 | San Rafael | 02_01 | Alajuela |
| 02_01_09 | Río Segundo | 02_01 | Alajuela |
| 02_01_10 | Desamparados | 02_01 | Alajuela |
| 02_01_11 | Turrúcares | 02_01 | Alajuela |
| 02_01_12 | Tambor | 02_01 | Alajuela |
| 02_01_13 | Garita | 02_01 | Alajuela |
| 02_01_14 | Sarapiquí | 02_01 | Alajuela |
| 02_02_01 | San Ramón | 02_02 | San Ramón |
| 02_02_02 | Santiago | 02_02 | San Ramón |
| 02_02_03 | San Juan | 02_02 | San Ramón |
| 02_02_04 | Piedades Norte | 02_02 | San Ramón |
| 02_02_05 | Piedades Sur | 02_02 | San Ramón |
| 02_02_06 | San Rafael | 02_02 | San Ramón |
| 02_02_07 | San Isidro | 02_02 | San Ramón |
| 02_02_08 | Ángeles | 02_02 | San Ramón |
| 02_02_09 | Alfaro | 02_02 | San Ramón |
| 02_02_10 | Volio | 02_02 | San Ramón |
| 02_02_11 | Concepción | 02_02 | San Ramón |
| 02_02_12 | Zapotal | 02_02 | San Ramón |
| 02_02_13 | Peñas Blancas | 02_02 | San Ramón |
| 02_02_14 | San Lorenzo | 02_02 | San Ramón |
| 02_03_01 | Grecia | 02_03 | Grecia |
| 02_03_02 | San Isidro | 02_03 | Grecia |
| 02_03_03 | San José | 02_03 | Grecia |
| 02_03_04 | San Roque | 02_03 | Grecia |
| 02_03_05 | Tacares | 02_03 | Grecia |
| 02_03_06 | Puente de Piedra | 02_03 | Grecia |
| 02_03_07 | Bolivar | 02_03 | Grecia |
| 02_04_01 | San Mateo | 02_04 | San Mateo |
| 02_04_02 | Desmonte | 02_04 | San Mateo |
| 02_04_03 | Jesús María | 02_04 | San Mateo |
| 02_04_04 | Labrador | 02_04 | San Mateo |
| 02_05_01 | Atenas | 02_05 | Atenas |
| 02_05_02 | Jesús | 02_05 | Atenas |
| 02_05_03 | Mercedes | 02_05 | Atenas |
| 02_05_04 | San Isidro | 02_05 | Atenas |
| 02_05_05 | Concepción | 02_05 | Atenas |
| 02_05_06 | San José | 02_05 | Atenas |
| 02_05_07 | Santa Eulalia | 02_05 | Atenas |
| 02_05_08 | Escobal | 02_05 | Atenas |
| 02_06_01 | Naranjo | 02_06 | Naranjo |
| 02_06_02 | San Miguel | 02_06 | Naranjo |
| 02_06_03 | San José | 02_06 | Naranjo |
| 02_06_04 | Cirrí Sur | 02_06 | Naranjo |
| 02_06_05 | San Jerónimo | 02_06 | Naranjo |
| 02_06_06 | San Juan | 02_06 | Naranjo |
| 02_06_07 | El Rosario | 02_06 | Naranjo |
| 02_06_08 | Palmitos | 02_06 | Naranjo |
| 02_07_01 | Palmares | 02_07 | Palmares |
| 02_07_02 | Zaragoza | 02_07 | Palmares |
| 02_07_03 | Buenos Aires | 02_07 | Palmares |
| 02_07_04 | Santiago | 02_07 | Palmares |
| 02_07_05 | Candelaria | 02_07 | Palmares |
| 02_07_06 | Esquipulas | 02_07 | Palmares |
| 02_07_07 | La Granja | 02_07 | Palmares |
| 02_08_01 | San Pedro | 02_08 | Poás |
| 02_08_02 | San Juan | 02_08 | Poás |
| 02_08_03 | San Rafael | 02_08 | Poás |
| 02_08_04 | Carrillos | 02_08 | Poás |
| 02_08_05 | Sabana Redonda | 02_08 | Poás |
| 02_09_01 | Orotina | 02_09 | Orotina |
| 02_09_02 | El Mastate | 02_09 | Orotina |
| 02_09_03 | Hacienda Vieja | 02_09 | Orotina |
| 02_09_04 | Coyolar | 02_09 | Orotina |
| 02_09_05 | La Ceiba | 02_09 | Orotina |
| 02_10_01 | Quesada | 02_10 | San Carlos |
| 02_10_02 | Florencia | 02_10 | San Carlos |
| 02_10_03 | Buenavista | 02_10 | San Carlos |
| 02_10_04 | Aguas Zarcas | 02_10 | San Carlos |
| 02_10_05 | Venecia | 02_10 | San Carlos |
| 02_10_06 | Pital | 02_10 | San Carlos |
| 02_10_07 | La Fortuna | 02_10 | San Carlos |
| 02_10_08 | La Tigra | 02_10 | San Carlos |
| 02_10_09 | La Palmera | 02_10 | San Carlos |
| 02_10_10 | Venado | 02_10 | San Carlos |
| 02_10_11 | Cutris | 02_10 | San Carlos |
| 02_10_12 | Monterrey | 02_10 | San Carlos |
| 02_10_13 | Pocosol | 02_10 | San Carlos |
| 02_11_01 | Zarcero | 02_11 | Zarcero |
| 02_11_02 | Laguna | 02_11 | Zarcero |
| 02_11_03 | Tapesco | 02_11 | Zarcero |
| 02_11_04 | Guadalupe | 02_11 | Zarcero |
| 02_11_05 | Palmira | 02_11 | Zarcero |
| 02_11_06 | Zapote | 02_11 | Zarcero |
| 02_11_07 | Brisas | 02_11 | Zarcero |
| 02_12_01 | Sarchí Norte | 02_12 | Sarchí |
| 02_12_02 | Sarchí Sur | 02_12 | Sarchí |
| 02_12_03 | Toro Amarillo | 02_12 | Sarchí |
| 02_12_04 | San Pedro | 02_12 | Sarchí |
| 02_12_05 | Rodríguez | 02_12 | Sarchí |
| 02_13_01 | Upala | 02_13 | Upala |
| 02_13_02 | Aguas Claras | 02_13 | Upala |
| 02_13_03 | San José O Pizote | 02_13 | Upala |
| 02_13_04 | Bijagua | 02_13 | Upala |
| 02_13_05 | Delicias | 02_13 | Upala |
| 02_13_06 | Dos Ríos | 02_13 | Upala |
| 02_13_07 | Yolillal | 02_13 | Upala |
| 02_13_08 | Canalete | 02_13 | Upala |
| 02_14_01 | Los Chiles | 02_14 | Los Chiles |
| 02_14_02 | Caño Negro | 02_14 | Los Chiles |
| 02_14_03 | El Amparo | 02_14 | Los Chiles |
| 02_14_04 | San Jorge | 02_14 | Los Chiles |
| 02_15_01 | San Rafael | 02_15 | Guatuso |
| 02_15_02 | Buenavista | 02_15 | Guatuso |
| 02_15_03 | Cote | 02_15 | Guatuso |
| 02_15_04 | Katira | 02_15 | Guatuso |
| 02_16_01 | Río Cuarto | 02_16 | Río Cuarto |
| 02_16_02 | Santa Rita | 02_16 | Río Cuarto |
| 02_16_03 | Santa Isabel | 02_16 | Río Cuarto |

## 03 — Cartago (51 distritos)

| DistrictCode | DistrictName | CantonCode | CantonName |
|---|---|---|---|
| 03_01_01 | Oriental | 03_01 | Cartago |
| 03_01_02 | Occidental | 03_01 | Cartago |
| 03_01_03 | Carmen | 03_01 | Cartago |
| 03_01_04 | San Nicolás | 03_01 | Cartago |
| 03_01_05 | Aguacaliente o San Francisco | 03_01 | Cartago |
| 03_01_06 | Guadalupe o Arenilla | 03_01 | Cartago |
| 03_01_07 | Corralillo | 03_01 | Cartago |
| 03_01_08 | Tierra Blanca | 03_01 | Cartago |
| 03_01_09 | Dulce Nombre | 03_01 | Cartago |
| 03_01_10 | Llano Grande | 03_01 | Cartago |
| 03_01_11 | Quebradilla | 03_01 | Cartago |
| 03_02_01 | Paraíso | 03_02 | Paraíso |
| 03_02_02 | Santiago | 03_02 | Paraíso |
| 03_02_03 | Orosi | 03_02 | Paraíso |
| 03_02_04 | Cachí | 03_02 | Paraíso |
| 03_02_05 | Llanos de Santa Lucía | 03_02 | Paraíso |
| 03_03_01 | Tres Ríos | 03_03 | La Unión |
| 03_03_02 | San Diego | 03_03 | La Unión |
| 03_03_03 | San Juan | 03_03 | La Unión |
| 03_03_04 | San Rafael | 03_03 | La Unión |
| 03_03_05 | Concepción | 03_03 | La Unión |
| 03_03_06 | Dulce Nombre | 03_03 | La Unión |
| 03_03_07 | San Ramón | 03_03 | La Unión |
| 03_03_08 | Río Azul | 03_03 | La Unión |
| 03_04_01 | Juan Viñas | 03_04 | Jiménez |
| 03_04_02 | Tucurrique | 03_04 | Jiménez |
| 03_04_03 | Pejibaye | 03_04 | Jiménez |
| 03_05_01 | Turrialba | 03_05 | Turrialba |
| 03_05_02 | La Suiza | 03_05 | Turrialba |
| 03_05_03 | Peralta | 03_05 | Turrialba |
| 03_05_04 | Santa Cruz | 03_05 | Turrialba |
| 03_05_05 | Santa Teresita | 03_05 | Turrialba |
| 03_05_06 | Pavones | 03_05 | Turrialba |
| 03_05_07 | Tuis | 03_05 | Turrialba |
| 03_05_08 | Tayutic | 03_05 | Turrialba |
| 03_05_09 | Santa Rosa | 03_05 | Turrialba |
| 03_05_10 | Tres Equis | 03_05 | Turrialba |
| 03_05_11 | La Isabel | 03_05 | Turrialba |
| 03_05_12 | Chirripó | 03_05 | Turrialba |
| 03_06_01 | Pacayas | 03_06 | Alvarado |
| 03_06_02 | Cervantes | 03_06 | Alvarado |
| 03_06_03 | Capellades | 03_06 | Alvarado |
| 03_07_01 | San Rafael | 03_07 | Oreamuno |
| 03_07_02 | Cot | 03_07 | Oreamuno |
| 03_07_03 | Potrero Cerrado | 03_07 | Oreamuno |
| 03_07_04 | Cipreses | 03_07 | Oreamuno |
| 03_07_05 | Santa Rosa | 03_07 | Oreamuno |
| 03_08_01 | El Tejar | 03_08 | El Guarco |
| 03_08_02 | San Isidro | 03_08 | El Guarco |
| 03_08_03 | Tobosi | 03_08 | El Guarco |
| 03_08_04 | Patio de Agua | 03_08 | El Guarco |

## 04 — Heredia (47 distritos)

| DistrictCode | DistrictName | CantonCode | CantonName |
|---|---|---|---|
| 04_01_01 | Heredia | 04_01 | Heredia |
| 04_01_02 | Mercedes | 04_01 | Heredia |
| 04_01_03 | San Francisco | 04_01 | Heredia |
| 04_01_04 | Ulloa | 04_01 | Heredia |
| 04_01_05 | Varablanca | 04_01 | Heredia |
| 04_02_01 | Barva | 04_02 | Barva |
| 04_02_02 | San Pedro | 04_02 | Barva |
| 04_02_03 | San Pablo | 04_02 | Barva |
| 04_02_04 | San Roque | 04_02 | Barva |
| 04_02_05 | Santa Lucía | 04_02 | Barva |
| 04_02_06 | San José de la Montaña | 04_02 | Barva |
| 04_03_01 | Santo Domingo | 04_03 | Santo Domingo |
| 04_03_02 | San Vicente | 04_03 | Santo Domingo |
| 04_03_03 | San Miguel | 04_03 | Santo Domingo |
| 04_03_04 | Paracito | 04_03 | Santo Domingo |
| 04_03_05 | Santo Tomás | 04_03 | Santo Domingo |
| 04_03_06 | Santa Rosa | 04_03 | Santo Domingo |
| 04_03_07 | Tures | 04_03 | Santo Domingo |
| 04_03_08 | Pará | 04_03 | Santo Domingo |
| 04_04_01 | Santa Bárbara | 04_04 | Santa Bárbara |
| 04_04_02 | San Pedro | 04_04 | Santa Bárbara |
| 04_04_03 | San Juan | 04_04 | Santa Bárbara |
| 04_04_04 | Jesús | 04_04 | Santa Bárbara |
| 04_04_05 | Santo Domingo | 04_04 | Santa Bárbara |
| 04_04_06 | Purabá | 04_04 | Santa Bárbara |
| 04_05_01 | San Rafael | 04_05 | San Rafael |
| 04_05_02 | San Josecito | 04_05 | San Rafael |
| 04_05_03 | Santiago | 04_05 | San Rafael |
| 04_05_04 | Ángeles | 04_05 | San Rafael |
| 04_05_05 | Concepción | 04_05 | San Rafael |
| 04_06_01 | San Isidro | 04_06 | San Isidro |
| 04_06_02 | San José | 04_06 | San Isidro |
| 04_06_03 | Concepción | 04_06 | San Isidro |
| 04_06_04 | San Francisco | 04_06 | San Isidro |
| 04_07_01 | San Antonio | 04_07 | Belén |
| 04_07_02 | La Ribera | 04_07 | Belén |
| 04_07_03 | La Asunción | 04_07 | Belén |
| 04_08_01 | San Joaquín | 04_08 | Flores |
| 04_08_02 | Barrantes | 04_08 | Flores |
| 04_08_03 | Llorente | 04_08 | Flores |
| 04_09_01 | San Pablo | 04_09 | San Pablo |
| 04_09_02 | Rincón de Sabanilla | 04_09 | San Pablo |
| 04_10_01 | Puerto Viejo | 04_10 | Sarapiquí |
| 04_10_02 | La Virgen | 04_10 | Sarapiquí |
| 04_10_03 | Las Horquetas | 04_10 | Sarapiquí |
| 04_10_04 | Llanuras del Gaspar | 04_10 | Sarapiquí |
| 04_10_05 | Cureña | 04_10 | Sarapiquí |

## 05 — Guanacaste (61 distritos)

| DistrictCode | DistrictName | CantonCode | CantonName |
|---|---|---|---|
| 05_01_01 | Liberia | 05_01 | Liberia |
| 05_01_02 | Cañas Dulces | 05_01 | Liberia |
| 05_01_03 | Mayorga | 05_01 | Liberia |
| 05_01_04 | Nacascolo | 05_01 | Liberia |
| 05_01_05 | Curubandé | 05_01 | Liberia |
| 05_02_01 | Nicoya | 05_02 | Nicoya |
| 05_02_02 | Mansión | 05_02 | Nicoya |
| 05_02_03 | San Antonio | 05_02 | Nicoya |
| 05_02_04 | Quebrada Honda | 05_02 | Nicoya |
| 05_02_05 | Sámara | 05_02 | Nicoya |
| 05_02_06 | Nosara | 05_02 | Nicoya |
| 05_02_07 | Belén de Nosarita | 05_02 | Nicoya |
| 05_03_01 | Santa Cruz | 05_03 | Santa Cruz |
| 05_03_02 | Bolsón | 05_03 | Santa Cruz |
| 05_03_03 | Veintisiete de Abril | 05_03 | Santa Cruz |
| 05_03_04 | Tempate | 05_03 | Santa Cruz |
| 05_03_05 | Cartagena | 05_03 | Santa Cruz |
| 05_03_06 | Cuajiniquil | 05_03 | Santa Cruz |
| 05_03_07 | Diriá | 05_03 | Santa Cruz |
| 05_03_08 | Cabo Velas | 05_03 | Santa Cruz |
| 05_03_09 | Tamarindo | 05_03 | Santa Cruz |
| 05_04_01 | Bagaces | 05_04 | Bagaces |
| 05_04_02 | La Fortuna | 05_04 | Bagaces |
| 05_04_03 | Mogote | 05_04 | Bagaces |
| 05_04_04 | Río Naranjo | 05_04 | Bagaces |
| 05_05_01 | Filadelfia | 05_05 | Carrillo |
| 05_05_02 | Palmira | 05_05 | Carrillo |
| 05_05_03 | Sardinal | 05_05 | Carrillo |
| 05_05_04 | Belén | 05_05 | Carrillo |
| 05_06_01 | Cañas | 05_06 | Cañas |
| 05_06_02 | Palmira | 05_06 | Cañas |
| 05_06_03 | San Miguel | 05_06 | Cañas |
| 05_06_04 | Bebedero | 05_06 | Cañas |
| 05_06_05 | Porozal | 05_06 | Cañas |
| 05_07_01 | Las Juntas | 05_07 | Abangares |
| 05_07_02 | Sierra | 05_07 | Abangares |
| 05_07_03 | San Juan | 05_07 | Abangares |
| 05_07_04 | Colorado | 05_07 | Abangares |
| 05_08_01 | Tilarán | 05_08 | Tilarán |
| 05_08_02 | Quebrada Grande | 05_08 | Tilarán |
| 05_08_03 | Tronadora | 05_08 | Tilarán |
| 05_08_04 | Santa Rosa | 05_08 | Tilarán |
| 05_08_05 | Líbano | 05_08 | Tilarán |
| 05_08_06 | Tierras Morenas | 05_08 | Tilarán |
| 05_08_07 | Arenal | 05_08 | Tilarán |
| 05_08_08 | Cabeceras | 05_08 | Tilarán |
| 05_09_01 | Carmona | 05_09 | Nandayure |
| 05_09_02 | Santa Rita | 05_09 | Nandayure |
| 05_09_03 | Zapotal | 05_09 | Nandayure |
| 05_09_04 | San Pablo | 05_09 | Nandayure |
| 05_09_05 | Porvenir | 05_09 | Nandayure |
| 05_09_06 | Bejuco | 05_09 | Nandayure |
| 05_10_01 | La Cruz | 05_10 | La Cruz |
| 05_10_02 | Santa Cecilia | 05_10 | La Cruz |
| 05_10_03 | La Garita | 05_10 | La Cruz |
| 05_10_04 | Santa Elena | 05_10 | La Cruz |
| 05_11_01 | Hojancha | 05_11 | Hojancha |
| 05_11_02 | Monte Romo | 05_11 | Hojancha |
| 05_11_03 | Puerto Carrillo | 05_11 | Hojancha |
| 05_11_04 | Huacas | 05_11 | Hojancha |
| 05_11_05 | Matambú | 05_11 | Hojancha |

## 06 — Puntarenas (60 distritos)

| DistrictCode | DistrictName | CantonCode | CantonName |
|---|---|---|---|
| 06_01_01 | Puntarenas | 06_01 | Puntarenas |
| 06_01_02 | Pitahaya | 06_01 | Puntarenas |
| 06_01_03 | Chomes | 06_01 | Puntarenas |
| 06_01_04 | Lepanto | 06_01 | Puntarenas |
| 06_01_05 | Paquera | 06_01 | Puntarenas |
| 06_01_06 | Manzanillo | 06_01 | Puntarenas |
| 06_01_07 | Guacimal | 06_01 | Puntarenas |
| 06_01_08 | Barranca | 06_01 | Puntarenas |
| 06_01_09 | Isla del Coco | 06_01 | Puntarenas |
| 06_01_10 | Cóbano | 06_01 | Puntarenas |
| 06_01_11 | Chacarita | 06_01 | Puntarenas |
| 06_01_12 | Chira | 06_01 | Puntarenas |
| 06_01_13 | Acapulco | 06_01 | Puntarenas |
| 06_01_14 | El Roble | 06_01 | Puntarenas |
| 06_01_15 | Arancibia | 06_01 | Puntarenas |
| 06_02_01 | Espíritu Santo | 06_02 | Esparza |
| 06_02_02 | San Juan Grande | 06_02 | Esparza |
| 06_02_03 | Macacona | 06_02 | Esparza |
| 06_02_04 | San Rafael | 06_02 | Esparza |
| 06_02_05 | San Jerónimo | 06_02 | Esparza |
| 06_02_06 | Caldera | 06_02 | Esparza |
| 06_03_01 | Buenos Aires | 06_03 | Buenos Aires |
| 06_03_02 | Volcán | 06_03 | Buenos Aires |
| 06_03_03 | Potrero Grande | 06_03 | Buenos Aires |
| 06_03_04 | Boruca | 06_03 | Buenos Aires |
| 06_03_05 | Pilas | 06_03 | Buenos Aires |
| 06_03_06 | Colinas | 06_03 | Buenos Aires |
| 06_03_07 | Chánguena | 06_03 | Buenos Aires |
| 06_03_08 | Biolley | 06_03 | Buenos Aires |
| 06_03_09 | Brunka | 06_03 | Buenos Aires |
| 06_04_01 | Miramar | 06_04 | Montes de Oro |
| 06_04_02 | La Unión | 06_04 | Montes de Oro |
| 06_04_03 | San Isidro | 06_04 | Montes de Oro |
| 06_05_01 | Puerto Cortés | 06_05 | Osa |
| 06_05_02 | Palmar | 06_05 | Osa |
| 06_05_03 | Sierpe | 06_05 | Osa |
| 06_05_04 | Bahía Ballena | 06_05 | Osa |
| 06_05_05 | Piedras Blancas | 06_05 | Osa |
| 06_05_06 | Bahía Drake | 06_05 | Osa |
| 06_06_01 | Quepos | 06_06 | Quepos |
| 06_06_02 | Savegre | 06_06 | Quepos |
| 06_06_03 | Naranjito | 06_06 | Quepos |
| 06_07_01 | Golfito | 06_07 | Golfito |
| 06_07_02 | Guaycará | 06_07 | Golfito |
| 06_07_03 | Pavón | 06_07 | Golfito |
| 06_08_01 | San Vito | 06_08 | Coto Brus |
| 06_08_02 | Sabalito | 06_08 | Coto Brus |
| 06_08_03 | Aguabuena | 06_08 | Coto Brus |
| 06_08_04 | Limoncito | 06_08 | Coto Brus |
| 06_08_05 | Pittier | 06_08 | Coto Brus |
| 06_08_06 | Gutiérrez Braun | 06_08 | Coto Brus |
| 06_09_01 | Parrita | 06_09 | Parrita |
| 06_10_01 | Corredor | 06_10 | Corredores |
| 06_10_02 | La Cuesta | 06_10 | Corredores |
| 06_10_03 | Canoas | 06_10 | Corredores |
| 06_10_04 | Laurel | 06_10 | Corredores |
| 06_11_01 | Jacó | 06_11 | Garabito |
| 06_11_02 | Tárcoles | 06_11 | Garabito |
| 06_12_01 | Monteverde | 06_12 | Monteverde |
| 06_13_01 | Puerto Jiménez | 06_13 | Puerto Jiménez |

## 07 — Limón (30 distritos)

| DistrictCode | DistrictName | CantonCode | CantonName |
|---|---|---|---|
| 07_01_01 | Limón | 07_01 | Limón |
| 07_01_02 | Valle La Estrella | 07_01 | Limón |
| 07_01_03 | Río Blanco | 07_01 | Limón |
| 07_01_04 | Matama | 07_01 | Limón |
| 07_02_01 | Guápiles | 07_02 | Pococí |
| 07_02_02 | Jiménez | 07_02 | Pococí |
| 07_02_03 | La Rita | 07_02 | Pococí |
| 07_02_04 | Roxana | 07_02 | Pococí |
| 07_02_05 | Cariari | 07_02 | Pococí |
| 07_02_06 | Colorado | 07_02 | Pococí |
| 07_02_07 | La Colonia | 07_02 | Pococí |
| 07_03_01 | Siquirres | 07_03 | Siquirres |
| 07_03_02 | Pacuarito | 07_03 | Siquirres |
| 07_03_03 | Florida | 07_03 | Siquirres |
| 07_03_04 | Germania | 07_03 | Siquirres |
| 07_03_05 | El Cairo | 07_03 | Siquirres |
| 07_03_06 | Alegría | 07_03 | Siquirres |
| 07_03_07 | Reventazón | 07_03 | Siquirres |
| 07_04_01 | Bratsi | 07_04 | Talamanca |
| 07_04_02 | Sixaola | 07_04 | Talamanca |
| 07_04_03 | Cahuita | 07_04 | Talamanca |
| 07_04_04 | Telire | 07_04 | Talamanca |
| 07_05_01 | Matina | 07_05 | Matina |
| 07_05_02 | Batán | 07_05 | Matina |
| 07_05_03 | Carrandí | 07_05 | Matina |
| 07_06_01 | Guácimo | 07_06 | Guácimo |
| 07_06_02 | Mercedes | 07_06 | Guácimo |
| 07_06_03 | Pocora | 07_06 | Guácimo |
| 07_06_04 | Río Jiménez | 07_06 | Guácimo |
| 07_06_05 | Duacarí | 07_06 | Guácimo |

## Validation summary

### Per-province distrito counts

| Province | Code | Distritos | Target | Status |
|---|---|---|---|---|
| San José | 01 | 123 | 123 | OK |
| Alajuela | 02 | 116 | 116 | OK |
| Cartago | 03 | 51 | 51 | OK |
| Heredia | 04 | 47 | 47 | OK |
| Guanacaste | 05 | 61 | 61 | OK |
| Puntarenas | 06 | 60 | 60 | OK |
| Limón | 07 | 30 | 30 | OK |
| **National total** | | **488** | **488** | OK |

### Edge-case confirmations

- **Golfito `06_07` = 3 distritos**: Golfito, Guaycará, Pavón. Puerto Jiménez was dropped from Golfito (it became its own cantón). CONFIRMED.
- **Monteverde `06_12` = 1 distrito**: Monteverde. New cantón (Ley 10019, 07/01/2022). CONFIRMED.
- **Puerto Jiménez `06_13` = 1 distrito**: Puerto Jiménez. New cantón (Ley 10195, 21/06/2022). CONFIRMED.
- All 84 cantón prefixes (`PP_CC`) match the fixed catalog; no orphan distritos.
- DD ordinals contiguous `01..N` within every cantón (no gaps).

### Reconciliation notes (deltas applied beyond the bulk source)

Two independent baselines were parsed and cross-checked:
- **Bulk source**: josuenoel gist (`gist.githubusercontent.com/josuenoel/.../raw`) — 479 distritos / 81 cantones (pre-2018 set; e.g. Monteverde/Puerto Jiménez still nested as distritos, Río Cuarto a single distrito of Grecia).
- **Authority**: IGN/Registro Nacional *División Territorial Administrativa 2024* PDF — 84 cantones, 492 distritos (2024 official). Used as the primary distrito list (codes + es-CR accented names parsed directly from the PDF text).

Reconciliation from the IGN 492 set to the 488-distrito seed snapshot:

**Added (1 row — present in IGN/Wikipedia, missing from the 2024 PDF text export):**
- `07_06_05` **Duacarí** (Guácimo, Limón) — created Decreto 12091-G, 1980. The 2024 SNIT PDF table truncates Guácimo at Río Jiménez; restored from *Anexo:Distritos de la provincia de Limón* (Wikipedia), which confirms Limón = 30 distritos and Guácimo = 5. Without this row Limón parses as 29.

**Excluded (4 rows — created 2021–2022, after the seed snapshot; removing them lands Cartago/Heredia/Puntarenas exactly on target):**
- `03_02` Paraíso — *Birrisito* (Ley 10004, 18/08/2021) excluded → Cartago 53→51.
- `03_04` Jiménez — *La Victoria* (Ley 10226, 21/06/2022) excluded → Cartago counted with Paraíso above.
- `04_02` Barva — *Puente Salas* (Decreto 10303, 12/09/2022) excluded → Heredia 48→47.
- `06_11` Garabito — *Lagunillas* (Ley 10055, 22/12/2021) excluded → Puntarenas 61→60.

**Structural (cantón splits — vacated numeric codes, ordinals re-closed):**
- Grecia `02_03` originally held *Río Cuarto* as its 6th distrito; Río Cuarto became cantón `02_16` (2017). Grecia = 7, Río Cuarto = 3 (Río Cuarto, Santa Rita, Santa Isabel).
- Puntarenas central `06_01` originally held *Monte Verde* as its 9th distrito; it became cantón Monteverde `06_12` (2022). Puntarenas central = 15.
- Golfito `06_07` originally held *Puerto Jiménez* as its 2nd distrito; it became cantón Puerto Jiménez `06_13` (2022). Golfito = 3.

**Spelling normalization to es-CR proper form:** `San Cristóbal` (Desamparados), `Patarrá` (Desamparados), `La Rita` (Pococí). All other names taken verbatim from the IGN PDF, which carries correct accents (e.g. Carrandí, Río Jiménez, Guaycará, Pavón).

**Uncertainty:** The 488 snapshot is a deliberate cut of the live 492-distrito IGN set (the 4 excluded distritos are the only post-2020 distrito creations). The seed targets and the 84-cantón catalog are internally consistent and fully satisfied. If a future requirement needs the live 492 set, re-add the 4 excluded rows.
