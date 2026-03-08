-- 1. Crear la tabla para almacenar la medición más reciente de cada variable por estación
CREATE TABLE IF NOT EXISTS public.ultimas_mediciones (
    dcp_id VARCHAR,
    variable VARCHAR,
    ts TIMESTAMP WITH TIME ZONE,
    valor REAL,
    tipo VARCHAR,
    PRIMARY KEY (dcp_id, variable)
);

-- 2. Función del trigger para actualizar o insertar la medición más reciente
CREATE OR REPLACE FUNCTION public.trg_actualizar_ultima_medicion()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
BEGIN
    INSERT INTO public.ultimas_mediciones (dcp_id, variable, ts, valor, tipo)
    VALUES (NEW.dcp_id, NEW.variable, NEW.ts, NEW.valor, NEW.tipo)
    ON CONFLICT (dcp_id, variable) DO UPDATE SET
        ts = CASE WHEN EXCLUDED.ts >= ultimas_mediciones.ts THEN EXCLUDED.ts ELSE ultimas_mediciones.ts END,
        valor = CASE WHEN EXCLUDED.ts >= ultimas_mediciones.ts THEN EXCLUDED.valor ELSE ultimas_mediciones.valor END,
        tipo = CASE WHEN EXCLUDED.ts >= ultimas_mediciones.ts THEN EXCLUDED.tipo ELSE ultimas_mediciones.tipo END;
    
    RETURN NEW;
END;
$$;

-- 3. Crear el trigger sobre la tabla dcp_datos
DROP TRIGGER IF EXISTS trg_update_last_val ON public.dcp_datos;

CREATE TRIGGER trg_update_last_val
AFTER INSERT ON public.dcp_datos
FOR EACH ROW
EXECUTE FUNCTION public.trg_actualizar_ultima_medicion();
