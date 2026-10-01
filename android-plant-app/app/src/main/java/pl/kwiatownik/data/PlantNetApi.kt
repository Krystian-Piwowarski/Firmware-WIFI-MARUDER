package pl.kwiatownik.data

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.HttpUrl.Companion.toHttpUrl
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.MultipartBody
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import org.json.JSONObject
import java.io.IOException

class PlantNetException(message: String) : Exception(message)

/** Klient API Pl@ntNet: https://my.plantnet.org/doc */
class PlantNetApi(private val client: OkHttpClient) {

    suspend fun identify(apiKey: String, jpeg: ByteArray): List<PlantMatch> =
        withContext(Dispatchers.IO) {
            if (apiKey.isBlank()) {
                throw PlantNetException(
                    "Brak klucza API Pl@ntNet. Załóż darmowe konto na my.plantnet.org " +
                        "i wklej klucz w Ustawieniach."
                )
            }
            val url = "https://my-api.plantnet.org/v2/identify/all".toHttpUrl().newBuilder()
                .addQueryParameter("api-key", apiKey.trim())
                .addQueryParameter("lang", "pl")
                .addQueryParameter("include-related-images", "true")
                .addQueryParameter("nb-results", "5")
                .build()
            val body = MultipartBody.Builder()
                .setType(MultipartBody.FORM)
                .addFormDataPart("images", "plant.jpg", jpeg.toRequestBody("image/jpeg".toMediaType()))
                .addFormDataPart("organs", "auto")
                .build()
            val request = Request.Builder().url(url).post(body).build()

            try {
                client.newCall(request).execute().use { resp ->
                    val text = resp.body?.string().orEmpty()
                    when (resp.code) {
                        200 -> parse(text)
                        404 -> emptyList()
                        401, 403 -> throw PlantNetException(
                            "Nieprawidłowy klucz API Pl@ntNet. Sprawdź go w Ustawieniach."
                        )
                        429 -> throw PlantNetException(
                            "Wykorzystano dzienny limit rozpoznań Pl@ntNet. Spróbuj jutro."
                        )
                        else -> throw PlantNetException("Błąd serwera Pl@ntNet (kod ${resp.code}).")
                    }
                }
            } catch (e: IOException) {
                throw PlantNetException("Brak połączenia z internetem lub serwer nie odpowiada.")
            }
        }

    private fun parse(text: String): List<PlantMatch> {
        val results = JSONObject(text).optJSONArray("results") ?: return emptyList()
        return (0 until results.length()).map { i ->
            val r = results.getJSONObject(i)
            val species = r.getJSONObject("species")
            val names = species.optJSONArray("commonNames")
            val image = r.optJSONArray("images")?.optJSONObject(0)?.optJSONObject("url")
            PlantMatch(
                scientificName = species.optString("scientificNameWithoutAuthor"),
                author = species.optString("scientificNameAuthorship"),
                genus = species.optJSONObject("genus")?.optString("scientificNameWithoutAuthor").orEmpty(),
                family = species.optJSONObject("family")?.optString("scientificNameWithoutAuthor").orEmpty(),
                commonNames = names?.let { a -> (0 until a.length()).map { a.getString(it) } }.orEmpty(),
                score = r.optDouble("score", 0.0),
                imageUrl = image?.optString("m")?.takeIf { it.isNotBlank() },
            )
        }.take(5)
    }
}
