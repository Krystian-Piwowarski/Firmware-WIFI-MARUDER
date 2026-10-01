package pl.kwiatownik.ui

import android.app.Application
import android.net.Uri
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.launch
import okhttp3.OkHttpClient
import pl.kwiatownik.data.CareGuide
import pl.kwiatownik.data.CareRepository
import pl.kwiatownik.data.ImageUtils
import pl.kwiatownik.data.PlantMatch
import pl.kwiatownik.data.PlantNetApi
import pl.kwiatownik.data.PlantNetException
import pl.kwiatownik.data.Settings
import pl.kwiatownik.data.WikiSummary
import pl.kwiatownik.data.WikipediaApi
import java.util.concurrent.TimeUnit

sealed interface Screen {
    data object Home : Screen
    data object Results : Screen
    data object Detail : Screen
    data object Atlas : Screen
    data object Settings : Screen
}

sealed interface IdentifyState {
    data object Idle : IdentifyState
    data object Loading : IdentifyState
    data class Success(val matches: List<PlantMatch>) : IdentifyState
    data class Error(val message: String) : IdentifyState
}

sealed interface WikiState {
    data object Loading : WikiState
    data class Loaded(val summary: WikiSummary) : WikiState
    data object NotFound : WikiState
}

/** Wszystko, co pokazuje ekran szczegółów rośliny. */
data class PlantDetail(
    val displayName: String,
    val scientificName: String,
    val genus: String,
    val family: String,
    val otherNames: List<String>,
    val score: Double?,
    val referenceImageUrl: String?,
    val care: CareGuide?,
)

class PlantViewModel(app: Application) : AndroidViewModel(app) {

    private val http = OkHttpClient.Builder()
        .connectTimeout(20, TimeUnit.SECONDS)
        .readTimeout(60, TimeUnit.SECONDS)
        .build()
    private val plantNet = PlantNetApi(http)
    private val wikipedia = WikipediaApi(http)
    val careRepository = CareRepository(app)
    val settings = Settings(app)

    val backStack = mutableStateListOf<Screen>(Screen.Home)
    val current: Screen get() = backStack.last()

    var photoUri by mutableStateOf<Uri?>(null)
        private set
    var identifyState by mutableStateOf<IdentifyState>(IdentifyState.Idle)
        private set
    var detail by mutableStateOf<PlantDetail?>(null)
        private set
    var wikiState by mutableStateOf<WikiState>(WikiState.Loading)
        private set

    private var identifyJob: Job? = null
    private var wikiJob: Job? = null

    fun navigate(screen: Screen) {
        backStack.add(screen)
    }

    fun back(): Boolean {
        if (backStack.size <= 1) return false
        backStack.removeAt(backStack.lastIndex)
        return true
    }

    fun identify(uri: Uri) {
        photoUri = uri
        identifyState = IdentifyState.Loading
        if (current != Screen.Results) navigate(Screen.Results)
        identifyJob?.cancel()
        identifyJob = viewModelScope.launch {
            identifyState = try {
                val jpeg = ImageUtils.prepareJpeg(getApplication(), uri)
                IdentifyState.Success(plantNet.identify(settings.plantNetApiKey, jpeg))
            } catch (e: PlantNetException) {
                IdentifyState.Error(e.message ?: "Nieznany błąd.")
            } catch (e: Exception) {
                if (e is kotlinx.coroutines.CancellationException) throw e
                IdentifyState.Error("Nie udało się przetworzyć zdjęcia: ${e.message}")
            }
        }
    }

    fun retry() {
        photoUri?.let { identify(it) }
    }

    fun openMatch(match: PlantMatch) {
        val care = careRepository.find(match.scientificName, match.genus)
        val name = match.commonNames.firstOrNull()?.replaceFirstChar { it.uppercase() }
            ?: care?.namePl
            ?: match.scientificName
        showDetail(
            PlantDetail(
                displayName = name,
                scientificName = match.scientificName,
                genus = match.genus,
                family = match.family,
                otherNames = match.commonNames.drop(1),
                score = match.score,
                referenceImageUrl = match.imageUrl,
                care = care,
            )
        )
    }

    fun openCareGuide(care: CareGuide) {
        showDetail(
            PlantDetail(
                displayName = care.namePl,
                scientificName = care.latin,
                genus = care.latin.substringBefore(' '),
                family = "",
                otherNames = emptyList(),
                score = null,
                referenceImageUrl = null,
                care = care,
            )
        )
    }

    private fun showDetail(d: PlantDetail) {
        detail = d
        wikiState = WikiState.Loading
        navigate(Screen.Detail)
        wikiJob?.cancel()
        wikiJob = viewModelScope.launch {
            val summary = wikipedia.findSummary(d.scientificName, d.genus)
            wikiState = if (summary != null) WikiState.Loaded(summary) else WikiState.NotFound
        }
    }
}
