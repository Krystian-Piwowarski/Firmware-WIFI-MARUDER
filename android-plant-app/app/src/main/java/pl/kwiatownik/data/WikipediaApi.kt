package pl.kwiatownik.data

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.OkHttpClient
import okhttp3.Request
import org.json.JSONObject
import java.net.URLEncoder

/** Pobiera krótkie streszczenie artykułu z Wikipedii (REST API). */
class WikipediaApi(private val client: OkHttpClient) {

    /** Szuka gatunku na pl.wikipedia, potem rodzaju, a na końcu gatunku na en.wikipedia. */
    suspend fun findSummary(scientificName: String, genus: String): WikiSummary? {
        summary("pl", scientificName)?.let { return it }
        if (genus.isNotBlank() && !genus.equals(scientificName, ignoreCase = true)) {
            summary("pl", genus)?.let { return it.copy(isGenusOnly = true) }
        }
        return summary("en", scientificName)
    }

    private suspend fun summary(lang: String, title: String): WikiSummary? =
        withContext(Dispatchers.IO) {
            val encoded = URLEncoder.encode(title.trim().replace(' ', '_'), "UTF-8")
            val request = Request.Builder()
                .url("https://$lang.wikipedia.org/api/rest_v1/page/summary/$encoded")
                .header("User-Agent", "Kwiatownik/1.0 (aplikacja Android do rozpoznawania roślin)")
                .build()
            runCatching {
                client.newCall(request).execute().use { resp ->
                    if (!resp.isSuccessful) return@use null
                    val json = JSONObject(resp.body?.string().orEmpty())
                    if (json.optString("type") != "standard") return@use null
                    val extract = json.optString("extract")
                    if (extract.isBlank()) return@use null
                    WikiSummary(
                        title = json.optString("title", title),
                        extract = extract,
                        imageUrl = json.optJSONObject("thumbnail")?.optString("source")?.takeIf { it.isNotBlank() },
                        pageUrl = json.optJSONObject("content_urls")?.optJSONObject("mobile")
                            ?.optString("page")?.takeIf { it.isNotBlank() },
                        isGenusOnly = false,
                    )
                }
            }.getOrNull()
        }
}
