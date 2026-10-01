package pl.kwiatownik.data

import android.content.Context
import pl.kwiatownik.BuildConfig

class Settings(context: Context) {
    private val prefs = context.getSharedPreferences("settings", Context.MODE_PRIVATE)

    var plantNetApiKey: String
        get() = prefs.getString(KEY_API, null)?.takeIf { it.isNotBlank() } ?: BuildConfig.PLANTNET_API_KEY
        set(value) = prefs.edit().putString(KEY_API, value.trim()).apply()

    private companion object {
        const val KEY_API = "plantnet_api_key"
    }
}
