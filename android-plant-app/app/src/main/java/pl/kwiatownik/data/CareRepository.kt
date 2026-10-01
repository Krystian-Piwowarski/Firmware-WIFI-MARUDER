package pl.kwiatownik.data

import android.content.Context
import org.json.JSONArray
import org.json.JSONObject

/** Wbudowana baza porad uprawowych. Dopasowuje najpierw pełną nazwę gatunku, potem rodzaj. */
class CareRepository(private val context: Context) {

    val all: List<CareGuide> by lazy { load() }

    fun find(scientificName: String, genus: String): CareGuide? {
        val species = normalize(scientificName)
        all.firstOrNull { g -> g.match.any { normalize(it) == species } }?.let { return it }
        // Nazwa bez trzeciego członu (np. odmiany / podgatunku).
        val twoWords = species.split(' ').take(2).joinToString(" ")
        all.firstOrNull { g -> g.match.any { normalize(it) == twoWords } }?.let { return it }
        val gen = normalize(genus.ifBlank { species.substringBefore(' ') })
        return all.firstOrNull { g -> g.match.any { normalize(it) == gen } }
    }

    private fun normalize(name: String) =
        name.lowercase().replace("×", " ").replace(Regex("\\s+"), " ").trim()

    private fun load(): List<CareGuide> {
        val text = context.assets.open("plants_care.json").bufferedReader(Charsets.UTF_8).use { it.readText() }
        val arr = JSONArray(text)
        return (0 until arr.length()).map { parse(arr.getJSONObject(it)) }.sortedBy { it.namePl }
    }

    private fun parse(o: JSONObject) = CareGuide(
        id = o.getString("id"),
        match = o.getJSONArray("match").strings(),
        namePl = o.getString("namePl"),
        latin = o.getString("latin"),
        type = o.optString("type"),
        difficulty = o.optString("difficulty"),
        description = o.optString("description"),
        light = o.optString("light"),
        water = o.optString("water"),
        soil = o.optString("soil"),
        temperature = o.optString("temperature"),
        fertilizing = o.optString("fertilizing"),
        flowering = o.optString("flowering"),
        pruning = o.optString("pruning"),
        tips = o.optJSONArray("tips")?.strings().orEmpty(),
        avoid = o.optJSONArray("avoid")?.strings().orEmpty(),
        toxicity = o.optString("toxicity"),
    )

    private fun JSONArray.strings() = (0 until length()).map { getString(it) }
}
