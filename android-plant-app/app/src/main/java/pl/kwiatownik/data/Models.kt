package pl.kwiatownik.data

/** Jedna propozycja gatunku zwrócona przez Pl@ntNet. */
data class PlantMatch(
    val scientificName: String,
    val author: String,
    val genus: String,
    val family: String,
    val commonNames: List<String>,
    val score: Double,
    val imageUrl: String?,
)

/** Porady uprawowe z wbudowanej bazy (assets/plants_care.json). */
data class CareGuide(
    val id: String,
    val match: List<String>,
    val namePl: String,
    val latin: String,
    val type: String,
    val difficulty: String,
    val description: String,
    val light: String,
    val water: String,
    val soil: String,
    val temperature: String,
    val fertilizing: String,
    val flowering: String,
    val pruning: String,
    val tips: List<String>,
    val avoid: List<String>,
    val toxicity: String,
)

data class WikiSummary(
    val title: String,
    val extract: String,
    val imageUrl: String?,
    val pageUrl: String?,
    /** true, gdy znaleziono tylko opis rodzaju, a nie konkretnego gatunku. */
    val isGenusOnly: Boolean,
)
